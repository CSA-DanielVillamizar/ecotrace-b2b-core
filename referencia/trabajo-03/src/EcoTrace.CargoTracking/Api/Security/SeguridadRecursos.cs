using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.CargoTracking.Api.Security;

/// <summary>
/// Este archivo se repite igual en Fleet, Cargo y Billing a propósito: compartirlo como proyecto obligaría a
/// redesplegar los tres juntos (ver docs/decisiones-de-diseno.md, decisión 2). Cada Resource Server valida los
/// tokens de Identity <b>por su cuenta</b>, con la clave pública (ADR 0004).
/// </summary>
public static class Audiencias
{
    public const string Identity = "ecotrace-identity";
    public const string Fleet = "ecotrace-fleet";
    public const string Cargo = "ecotrace-cargo";
    public const string Billing = "ecotrace-billing";
}

public static class Roles
{
    public const string Conductor = "Conductor";
    public const string Supervisor = "Supervisor";
    public const string Administrador = "Administrador";
    public const string Auditor = "Auditor";
    public const string Servicio = "Servicio";
}

public static class Reclamos
{
    public const string Sub = "sub";
    public const string TenantId = "tenant_id";
    public const string Rol = "role";
    public const string Plataforma = "plataforma";
}

public static class Politicas
{
    public const string Servicio = "Servicio";
    public const string Operador = "Operador";
    public const string OperadorOServicio = "OperadorOServicio";
}

/// <summary>Lectura de los datos de quien llama, siempre desde el token y nunca desde un parámetro del cliente.</summary>
public static class UsuarioActual
{
    public static string? Sub(this ClaimsPrincipal usuario) => usuario.FindFirstValue(Reclamos.Sub);

    public static string? Rol(this ClaimsPrincipal usuario) => usuario.FindFirstValue(Reclamos.Rol);

    public static Guid? TenantId(this ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(Reclamos.TenantId), out var id) ? id : null;

    public static bool EsServicio(this ClaimsPrincipal usuario) => usuario.Rol() == Roles.Servicio;

    public static bool EsOperador(this ClaimsPrincipal usuario) =>
        usuario.Rol() == Roles.Administrador && usuario.FindFirstValue(Reclamos.Plataforma) == "true";

    /// <summary>True si la organización del token es una de las dadas (las dos de una carga o de un pago, por ejemplo).</summary>
    public static bool PerteneceA(this ClaimsPrincipal usuario, params Guid[] organizaciones) =>
        usuario.TenantId() is { } propia && organizaciones.Contains(propia);
}

/// <summary>Configuración de los tokens que acepta este módulo (sección <c>Jwt</c>). Solo el emisor: la clave sale del JWKS.</summary>
public sealed class OpcionesJwt
{
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Cada cuánto se vuelve a bajar el JWKS aunque nadie lo pida.</summary>
    public int RenovarJwksCadaMinutos { get; set; } = 60;

    /// <summary>
    /// Mínimo entre dos descargas forzadas por un <c>kid</c> desconocido. Sin este límite, quien manda tokens con
    /// kid inventados haría que este módulo bombardee a Identity.
    /// </summary>
    public int MinimoEntreRenovacionesSegundos { get; set; } = 30;
}

public static class SeguridadRecursos
{
    /// <summary>Nombre del cliente HTTP con el que se descarga el JWKS de Identity. Las pruebas lo reemplazan.</summary>
    public const string ClienteJwks = "identity-jwks";

    public static WebApplicationBuilder AddSeguridad(this WebApplicationBuilder builder, string audiencia)
    {
        builder.Services.Configure<OpcionesJwt>(builder.Configuration.GetSection("Jwt"));

        builder.Services.AddHttpClient(ClienteJwks, (sp, c) =>
            c.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Servicios:Identity:BaseUrl"] ?? "http://localhost:5101"));

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<Microsoft.Extensions.Options.IOptions<OpcionesJwt>, IHttpClientFactory, ILoggerFactory, IConfiguration>((o, jwt, fabricaHttp, fabricaLogs, configuracion) =>
            {
                var registro = fabricaLogs.CreateLogger("EcoTrace.Seguridad");
                if (string.IsNullOrWhiteSpace(jwt.Value.Issuer))
                {
                    throw new InvalidOperationException("Jwt:Issuer es obligatorio.");
                }

                // El JWKS se descarga una vez y se guarda en memoria. Se renueva cada hora, o cuando llega un kid
                // que no conoce (con un mínimo entre renovaciones), nunca en cada solicitud. Si Identity cae, la
                // última copia sigue sirviendo y los tokens válidos siguen validándose.
                var direccion = new Uri(new Uri(configuracion["Servicios:Identity:BaseUrl"] ?? "http://localhost:5101"), "/.well-known/jwks.json").ToString();
                o.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    direccion, new RecuperadorDeJwks(),
                    new HttpDocumentRetriever(fabricaHttp.CreateClient(ClienteJwks)) { RequireHttps = false })
                {
                    AutomaticRefreshInterval = TimeSpan.FromMinutes(jwt.Value.RenovarJwksCadaMinutos),
                    RefreshInterval = TimeSpan.FromSeconds(jwt.Value.MinimoEntreRenovacionesSegundos)
                };

                o.MapInboundClaims = false;
                o.RequireHttpsMetadata = false;
                o.RefreshOnIssuerKeyNotFound = true;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    // Se verifica en este orden: firma, exp, iss y aud. Cualquier falla es 401 y no se intenta arreglar el token.
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = audiencia,
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
}

/// <summary>Convierte el JWKS de Identity en la configuración de claves que espera el validador de JwtBearer.</summary>
internal sealed class RecuperadorDeJwks : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string direccion, IDocumentRetriever recuperador, CancellationToken ct)
    {
        var json = await recuperador.GetDocumentAsync(direccion, ct);
        var configuracion = new OpenIdConnectConfiguration { JsonWebKeySet = new JsonWebKeySet(json) };
        foreach (var clave in configuracion.JsonWebKeySet.GetSigningKeys())
        {
            configuracion.SigningKeys.Add(clave);
        }

        return configuracion;
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
