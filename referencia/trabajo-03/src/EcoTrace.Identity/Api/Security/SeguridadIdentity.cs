using System.Security.Cryptography;
using EcoTrace.Identity.Domain;
using EcoTrace.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Identity.Api.Security;

/// <summary>
/// Un cliente de servicio (otro módulo de la plataforma) autorizado a pedir tokens de servicio.
/// Su secreto viene de configuración o de una variable de entorno, nunca del repositorio.
/// </summary>
public sealed class ClienteDeServicio
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Módulos de destino para los que este cliente puede pedir token.</summary>
    public string[] Audiencias { get; set; } = [];
}

/// <summary>
/// Registro de ejemplo con identificador fijo que solo existe en el entorno que lo declara (producción simulada). Es lo que
/// permite a scripts/verificar-release.ps1 comprobar que el backend que responde es el de producción y no el de desarrollo.
/// </summary>
public sealed class OpcionesVerificacion
{
    public Guid? TenantId { get; set; }

    public string? Nombre { get; set; }
}

public sealed class OpcionesPlataforma
{
    public string Nombre { get; set; } = "Operación EcoTrace";

    public string? AdminEmail { get; set; }

    public string? AdminPassword { get; set; }
}

/// <summary>Registra la autenticación y las políticas. Identity valida con su propia clave: no pregunta a nadie.</summary>
public static class SeguridadIdentity
{
    public static WebApplicationBuilder AddSeguridad(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<OpcionesJwt>(builder.Configuration.GetSection("Jwt"));
        builder.Services.Configure<OpcionesPlataforma>(builder.Configuration.GetSection("Plataforma"));
        builder.Services.Configure<OpcionesVerificacion>(builder.Configuration.GetSection("Verificacion"));
        builder.Services.Configure<List<ClienteDeServicio>>(builder.Configuration.GetSection("Servicios:Clientes"));

        builder.Services.AddSingleton(sp =>
            ClavesDeFirma.Cargar(sp.GetRequiredService<IOptions<OpcionesJwt>>().Value.PrivateKeyPem, sp.GetRequiredService<IHostEnvironment>()));
        builder.Services.AddSingleton<EmisorDeTokens>();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<ClavesDeFirma, IOptions<OpcionesJwt>, ILoggerFactory>((o, claves, jwt, fabrica) =>
            {
                var registro = fabrica.CreateLogger("EcoTrace.Seguridad");
                if (string.IsNullOrWhiteSpace(jwt.Value.Issuer))
                {
                    throw new InvalidOperationException("Jwt:Issuer es obligatorio.");
                }

                o.MapInboundClaims = false;
                o.RequireHttpsMetadata = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    // El orden de la verificación (firma, exp, iss, aud) lo fija el validador; cualquier falla es 401.
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = claves.Clave,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = Audiencias.Identity,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = Reclamos.Sub,
                    RoleClaimType = Reclamos.Rol
                };
                o.Events = RespuestasDeSeguridad.Eventos(registro);
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Politicas.Servicio, p => p.RequireRole(Roles.Servicio))
            .AddPolicy(Politicas.Operador, p => p.RequireRole(Roles.Administrador).RequireClaim(Reclamos.Plataforma, "true"))
            .AddPolicy(Politicas.OperadorOServicio, p => p.RequireAssertion(c =>
                c.User.IsInRole(Roles.Servicio)
                || (c.User.IsInRole(Roles.Administrador) && c.User.HasClaim(Reclamos.Plataforma, "true"))));

        return builder;
    }

    /// <summary>
    /// Falla al arrancar, y no en la primera solicitud, si falta la clave de firma o el emisor. Un servicio de
    /// autenticación mal configurado no debe llegar a quedar en pie.
    /// </summary>
    public static WebApplication ValidarSeguridadAlArrancar(this WebApplication app)
    {
        _ = app.Services.GetRequiredService<ClavesDeFirma>();
        if (string.IsNullOrWhiteSpace(app.Services.GetRequiredService<IOptions<OpcionesJwt>>().Value.Issuer))
        {
            throw new InvalidOperationException("Jwt:Issuer es obligatorio.");
        }

        return app;
    }

    private static async Task SembrarRegistroDeVerificacionAsync(IdentityDbContext db, OpcionesVerificacion opciones, DateTime ahora)
    {
        if (opciones.TenantId is not { } id || string.IsNullOrWhiteSpace(opciones.Nombre) || await db.Tenants.AnyAsync(t => t.TenantId == id))
        {
            return;
        }

        db.Tenants.Add(Tenant.Crear(opciones.Nombre, TenantType.Generador, ahora, id));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Crea, si faltan, la organización operadora y su administrador, con los datos de configuración. Es el único
    /// camino para crear al primer administrador: después, él crea a los demás por la API.
    /// </summary>
    public static async Task SembrarPlataformaAsync(this WebApplication app)
    {
        using var alcance = app.Services.CreateScope();
        var db = alcance.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var opciones = alcance.ServiceProvider.GetRequiredService<IOptions<OpcionesPlataforma>>().Value;
        var reloj = alcance.ServiceProvider.GetRequiredService<TimeProvider>();
        var registro = alcance.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("EcoTrace.Seguridad");

        if (string.IsNullOrWhiteSpace(opciones.AdminEmail) || string.IsNullOrWhiteSpace(opciones.AdminPassword))
        {
            registro.LogWarning("No hay Plataforma:AdminEmail y Plataforma:AdminPassword: no se siembra el administrador de la plataforma.");
            return;
        }

        var ahora = reloj.GetUtcNow().UtcDateTime;
        await SembrarRegistroDeVerificacionAsync(db, alcance.ServiceProvider.GetRequiredService<IOptions<OpcionesVerificacion>>().Value, ahora);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.TenantType == TenantType.Plataforma);
        if (tenant is null)
        {
            tenant = Tenant.Crear(opciones.Nombre, TenantType.Plataforma, ahora);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
        }

        var email = opciones.AdminEmail.Trim().ToLowerInvariant();
        if (!await db.Users.AnyAsync(u => u.Email == email))
        {
            var rol = await db.Roles.FirstAsync(r => r.Nombre == Roles.Administrador);
            var iteraciones = app.Configuration.GetValue("Seguridad:Pbkdf2Iteraciones", Contrasenas.IteracionesPorDefecto);
            var hash = Contrasenas.Hashear(Contrasenas.Validar(opciones.AdminPassword), iteraciones);
            db.Users.Add(User.Crear(tenant.TenantId, rol.RoleId, "Administrador de plataforma", email, ahora, hash));
            await db.SaveChangesAsync();
        }
    }
}

/// <summary>
/// Respuestas de seguridad iguales en todos los módulos: el cliente recibe un mensaje genérico y el motivo exacto va
/// solo al registro del servidor. Así un atacante no aprende si falló la firma, el vencimiento o la audiencia.
/// </summary>
public static class RespuestasDeSeguridad
{
    public static JwtBearerEvents Eventos(ILogger registro) => new()
    {
        OnAuthenticationFailed = contexto =>
        {
            // Nunca se registra el token, solo el tipo de falla.
            registro.LogWarning("Token rechazado ({Motivo}) en {Metodo} {Ruta}",
                contexto.Exception.GetType().Name, contexto.Request.Method, contexto.Request.Path);
            return Task.CompletedTask;
        },
        OnChallenge = async contexto =>
        {
            contexto.HandleResponse();
            contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await contexto.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
                title = "No autenticado",
                status = 401,
                detail = "Falta un token válido."
            }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
        },
        OnForbidden = async contexto =>
        {
            contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
            await contexto.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
                title = "Sin permiso",
                status = 403,
                detail = "No tienes permiso para esta operación."
            }, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
        }
    };
}
