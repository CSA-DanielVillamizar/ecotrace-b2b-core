using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.CargoTracking.Api.Security;

/// <summary>Los módulos de EcoTrace a los que puede ir dirigido un token. Cada token lleva en <c>aud</c> los que le sirven.</summary>
public static class Audiencias
{
    public const string Identity = "ecotrace-identity";
    public const string Fleet = "ecotrace-fleet";
    public const string Cargo = "ecotrace-cargo";
    public const string Billing = "ecotrace-billing";
}

/// <summary>Los roles que Identity pone en el claim <c>role</c>.</summary>
public static class Roles
{
    public const string Conductor = "Conductor";
    public const string Supervisor = "Supervisor";
    public const string Administrador = "Administrador";
    public const string Auditor = "Auditor";

    /// <summary>Un servicio interno (por ejemplo Billing). No es una persona y su token no trae organización.</summary>
    public const string Servicio = "Servicio";
}

/// <summary>Los nombres de los claims del token (ADR 0004).</summary>
public static class Reclamos
{
    public const string Sub = "sub";
    public const string TenantId = "tenant_id";
    public const string Rol = "role";
}

/// <summary>
/// Lectura de los datos de quien llama. Salen <b>siempre del token</b> y nunca de un parámetro del cliente: así una persona
/// no puede decir que es de otra organización cambiando lo que manda en la solicitud.
/// </summary>
public static class UsuarioActual
{
    /// <summary>La organización de quien llama, o null si el token no trae una (el de un servicio, por ejemplo).</summary>
    public static Guid? TenantId(this ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(Reclamos.TenantId), out var id) ? id : null;

    /// <summary>True si la organización del token es una de las dadas (las dos de una carga, por ejemplo).</summary>
    public static bool PerteneceA(this ClaimsPrincipal usuario, params Guid[] organizaciones) =>
        usuario.TenantId() is { } propia && organizaciones.Contains(propia);
}

/// <summary>
/// Qué tokens acepta este módulo (sección <c>Jwt</c> de la configuración). Solo se configura quién emite y para quién es:
/// la clave para comprobar la firma no está aquí, se descarga de Identity.
/// </summary>
public sealed class OpcionesJwt
{
    /// <summary>Quién emitió el token. Es distinto en cada entorno, así un token de desarrollo no sirve en producción.</summary>
    public string Emisor { get; set; } = string.Empty;

    /// <summary>Para quién es el token. En este módulo es <c>ecotrace-cargo</c>.</summary>
    public string Audiencia { get; set; } = string.Empty;

    /// <summary>Cada cuántos minutos se vuelve a bajar la clave pública aunque nadie lo pida.</summary>
    public int RenovarJwksCadaMinutos { get; set; } = 60;

    /// <summary>
    /// Mínimo de segundos entre dos descargas forzadas por una clave desconocida. Sin este límite, quien manda tokens con
    /// claves inventadas haría que este módulo bombardee a Identity.
    /// </summary>
    public int MinimoEntreRenovacionesSegundos { get; set; } = 30;
}

public static class SeguridadRecursos
{
    /// <summary>Nombre del cliente HTTP con el que se descarga la clave pública de Identity. Las pruebas lo reemplazan.</summary>
    public const string ClienteJwks = "identity-jwks";

    /// <summary>
    /// Hace de Cargo &amp; Tracking un Resource Server (ADR 0004): valida por su cuenta los tokens que emite Identity, sin
    /// llamarlo en cada solicitud. Comprueba, en este orden, la firma, que no haya vencido (<c>exp</c>), quién lo emitió
    /// (<c>iss</c>) y para quién es (<c>aud</c>). Si algo falla responde 401 y no intenta arreglar el token.
    /// </summary>
    public static WebApplicationBuilder AddSeguridad(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<OpcionesJwt>(builder.Configuration.GetSection("Jwt"));

        builder.Services.AddHttpClient(ClienteJwks, (proveedor, cliente) =>
            cliente.BaseAddress = new Uri(proveedor.GetRequiredService<IConfiguration>()["Servicios:Identity:BaseUrl"] ?? "http://localhost:5101"));

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<OpcionesJwt>, IHttpClientFactory, ILoggerFactory>((opciones, jwt, fabricaHttp, fabricaLogs) =>
            {
                var configurado = jwt.Value;
                if (string.IsNullOrWhiteSpace(configurado.Emisor) || string.IsNullOrWhiteSpace(configurado.Audiencia))
                {
                    // Un módulo sin saber a quién creerle no debe arrancar: es mejor que falle ahora y no que acepte tokens de cualquiera.
                    throw new InvalidOperationException("Jwt:Emisor y Jwt:Audiencia son obligatorios.");
                }

                // La clave pública de Identity se descarga una vez y se guarda en memoria. Se renueva cada hora, o cuando
                // llega un token firmado con una clave que no conoce (con un mínimo entre renovaciones). Nunca en cada
                // solicitud: si Identity se cae, la última copia sigue sirviendo y los tokens válidos siguen validándose.
                var cliente = fabricaHttp.CreateClient(ClienteJwks);
                var direccion = new Uri(cliente.BaseAddress!, "/.well-known/jwks.json").ToString();
                opciones.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    direccion, new RecuperadorDeJwks(), new HttpDocumentRetriever(cliente) { RequireHttps = false })
                {
                    AutomaticRefreshInterval = TimeSpan.FromMinutes(configurado.RenovarJwksCadaMinutos),
                    RefreshInterval = TimeSpan.FromSeconds(configurado.MinimoEntreRenovacionesSegundos)
                };

                // Los claims se leen con el nombre que trae el token (sub, role, tenant_id) y no con los nombres largos de Microsoft.
                opciones.MapInboundClaims = false;
                opciones.RequireHttpsMetadata = false;
                opciones.RefreshOnIssuerKeyNotFound = true;
                opciones.TokenValidationParameters = new TokenValidationParameters
                {
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,

                    // Solo RS256. Así un token con "alg: none" o firmado con otro algoritmo se rechaza sin discusión.
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],

                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ValidateIssuer = true,
                    ValidIssuer = configurado.Emisor,
                    ValidateAudience = true,
                    ValidAudience = configurado.Audiencia,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = Reclamos.Sub,
                    RoleClaimType = Reclamos.Rol
                };
                opciones.Events = RespuestasDeSeguridad.Eventos(fabricaLogs.CreateLogger("EcoTrace.Seguridad"));
            });

        builder.Services.AddAuthorization();
        return builder;
    }
}

/// <summary>Convierte la lista de claves públicas de Identity (JWKS) en la configuración que espera el validador de tokens.</summary>
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
/// Las respuestas cuando un token no sirve. Al cliente le llega un mensaje <b>genérico</b>, y el motivo exacto va solo al
/// registro del servidor: así quien esté probando un ataque no aprende si falló la firma, el vencimiento o la audiencia.
/// </summary>
public static class RespuestasDeSeguridad
{
    public static JwtBearerEvents Eventos(ILogger registro) => new()
    {
        OnAuthenticationFailed = contexto =>
        {
            // Nunca se registra el token, solo el tipo de falla.
            registro.LogWarning(
                "Token rechazado ({Motivo}) en {Metodo} {Ruta}",
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
            }, (JsonSerializerOptions?)null, "application/problem+json");
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
            }, (JsonSerializerOptions?)null, "application/problem+json");
        }
    };
}
