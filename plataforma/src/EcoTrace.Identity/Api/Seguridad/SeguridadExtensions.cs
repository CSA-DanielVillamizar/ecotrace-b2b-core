using EcoTrace.Identity.Domain;
using EcoTrace.Identity.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Usuario = EcoTrace.Identity.Domain.User;

namespace EcoTrace.Identity.Api.Seguridad;

/// <summary>
/// Identity es el Authorization Server (emite y publica la clave) y también Resource Server de sí mismo:
/// sus rutas administrativas e internas exigen un token válido (Trabajo 3, ADR 0004).
/// </summary>
public static class SeguridadExtensions
{
    private static readonly TimeSpan ToleranciaReloj = TimeSpan.FromSeconds(30);

    public static WebApplicationBuilder AddSeguridadIdentity(this WebApplicationBuilder builder)
    {
        var servicios = builder.Services;

        servicios.AddOptions<OpcionesJwt>()
            .BindConfiguration(OpcionesJwt.Seccion)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Emisor), "Falta Jwt:Emisor.")
            .Validate(o => o.Audiencias.Length > 0, "Falta Jwt:Audiencias.")
            .ValidateOnStart();
        servicios.AddOptions<OpcionesOperador>().BindConfiguration(OpcionesOperador.Seccion);

        servicios.AddSingleton(sp => ClavesDeFirma.Cargar(
            sp.GetRequiredService<IOptions<OpcionesJwt>>().Value,
            sp.GetRequiredService<IHostEnvironment>().ContentRootPath));
        servicios.AddSingleton<EmisorDeTokens>();
        servicios.AddSingleton<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
        servicios.AddSingleton<Contrasenas>();
        servicios.AddSingleton<AccesoOrganizacion>();
        servicios.AddSingleton<IAuthorizationHandler, ManejadorPlataforma>();

        servicios.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        servicios.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<ClavesDeFirma, IOptions<OpcionesJwt>>((o, claves, jwt) =>
            {
                // Sin esto ASP.NET renombra "role" y "sub" a los nombres largos de WS-Federation.
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    // Orden del contrato: firma, exp, iss y aud. Cualquier fallo es 401.
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = claves.ClavePublica,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = ToleranciaReloj,
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Emisor,
                    ValidateAudience = true,
                    ValidAudience = jwt.Value.AudienciaPropia,
                    NameClaimType = "sub",
                    RoleClaimType = Claims.Rol
                };
                o.Events = new JwtBearerEvents
                {
                    OnChallenge = ResponderNoAutenticadoAsync,
                    OnForbidden = contexto => EscribirProblemaAsync(
                        contexto.HttpContext, StatusCodes.Status403Forbidden, "Sin permiso",
                        "El token es válido pero no tiene permiso para esta operación.")
                };
            });

        servicios.AddAuthorizationBuilder()
            .AddPolicy(Politicas.Administrador, p => p.RequireRole(Roles.Administrador))
            .AddPolicy(Politicas.AdministradorPlataforma, p => p.RequireRole(Roles.Administrador).AddRequirements(new RequisitoPlataforma()))
            .AddPolicy(Politicas.Servicio, p => p.RequireRole(Roles.Servicio))
            .AddPolicy(Politicas.Usuario, p => p.RequireAuthenticatedUser().RequireAssertion(c => !c.User.IsInRole(Roles.Servicio)));

        servicios.ConfigureSwaggerGen(o =>
        {
            var esquema = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            };
            o.AddSecurityDefinition("Bearer", esquema);
            o.AddSecurityRequirement(new OpenApiSecurityRequirement { [esquema] = [] });
        });

        return builder;
    }

    public static WebApplication UseSeguridadIdentity(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    /// <summary>
    /// Siembra la organización operadora y su administrador inicial si no existen. Es idempotente:
    /// en cada arranque solo crea lo que falta y nunca cambia una contraseña ya asignada.
    /// </summary>
    public static WebApplication SembrarOperador(this WebApplication app)
    {
        var operador = app.Services.GetRequiredService<IOptions<OpcionesOperador>>().Value;
        if (operador.TenantId == Guid.Empty)
        {
            return app;
        }

        using var alcance = app.Services.CreateScope();
        var db = alcance.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var ahora = alcance.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;

        if (!db.Tenants.Any(t => t.TenantId == operador.TenantId))
        {
            db.Tenants.Add(Tenant.CrearOperador(operador.TenantId, operador.Nombre, ahora));
            db.SaveChanges();
        }

        var email = operador.AdminEmail?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(operador.AdminPassword) || db.Users.Any(u => u.Email == email))
        {
            return app;
        }

        Usuario.ValidarContrasena(operador.AdminPassword);
        var rol = db.Roles.Single(r => r.Nombre == Roles.Administrador);
        var admin = Usuario.Crear(operador.TenantId, rol.RoleId, "Administrador de la plataforma", email, ahora);
        admin.AsignarContrasenaHash(alcance.ServiceProvider.GetRequiredService<Contrasenas>().Calcular(admin, operador.AdminPassword));
        db.Users.Add(admin);
        db.SaveChanges();

        app.Logger.LogInformation("Administrador inicial de la plataforma sembrado ({UserId}).", admin.UserId);
        return app;
    }

    private static Task ResponderNoAutenticadoAsync(JwtBearerChallengeContext contexto)
    {
        contexto.HandleResponse();

        // El motivo exacto queda en el registro del servidor; al cliente solo le llega un 401 genérico.
        var motivo = contexto.AuthenticateFailure?.GetType().Name ?? "SinToken";
        contexto.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("EcoTrace.Identity.Seguridad")
            .LogInformation("Token rechazado en {Metodo} {Ruta}: {Motivo}",
                contexto.Request.Method, contexto.Request.Path, motivo);

        contexto.Response.Headers.WWWAuthenticate = "Bearer";
        return EscribirProblemaAsync(contexto.HttpContext, StatusCodes.Status401Unauthorized,
            "No autenticado", "Se requiere un token válido.");
    }

    private static async Task EscribirProblemaAsync(HttpContext http, int estado, string titulo, string detalle)
    {
        http.Response.StatusCode = estado;
        await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = estado, Title = titulo, Detail = detalle }
        });
    }
}
