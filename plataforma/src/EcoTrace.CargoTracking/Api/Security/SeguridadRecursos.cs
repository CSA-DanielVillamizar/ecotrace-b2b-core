using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.CargoTracking.Api.Security;

/// <summary>Los módulos de EcoTrace a los que puede ir dirigido un token. Cada token trae en <c>aud</c> los módulos para los que sirve.</summary>
public static class Audiencias
{
    /// <summary>El módulo de Identity (login y organizaciones).</summary>
    public const string Identity = "ecotrace-identity";
    /// <summary>El módulo de Fleet Management (vehículos y conductores).</summary>
    public const string Fleet = "ecotrace-fleet";
    /// <summary>Este módulo: Cargo &amp; Tracking.</summary>
    public const string Cargo = "ecotrace-cargo";
    /// <summary>El módulo de Billing &amp; Escrow (pagos).</summary>
    public const string Billing = "ecotrace-billing";
}

/// <summary>Los roles que Identity pone en el token, en el dato <c>role</c>.</summary>
public static class Roles
{
    /// <summary>Quien maneja y entrega la carga.</summary>
    public const string Conductor = "Conductor";
    /// <summary>Quien coordina a los conductores de una organización transportista.</summary>
    public const string Supervisor = "Supervisor";
    /// <summary>Quien administra una organización.</summary>
    public const string Administrador = "Administrador";
    /// <summary>Quien solo consulta.</summary>
    public const string Auditor = "Auditor";

    /// <summary>Un servicio interno (por ejemplo Billing). No es una persona y su token no trae organización.</summary>
    public const string Servicio = "Servicio";
}

/// <summary>Los nombres de los datos (claims) que trae el token (ADR 0004).</summary>
public static class Reclamos
{
    /// <summary>Quién es: el identificador del usuario.</summary>
    public const string Sub = "sub";
    /// <summary>De qué organización es.</summary>
    public const string TenantId = "tenant_id";
    /// <summary>Qué rol tiene.</summary>
    public const string Rol = "role";
}

/// <summary>
/// Lee los datos de quien hace la llamada. Salen <b>siempre del token</b> y nunca de lo que mande el cliente: así nadie puede
/// decir que es de otra organización cambiando lo que escribe en la solicitud.
/// </summary>
public static class UsuarioActual
{
    /// <summary>La organización de quien llama, o null si el token no trae una (el de un servicio, por ejemplo).</summary>
    public static Guid? TenantId(this ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(Reclamos.TenantId), out var id) ? id : null;

    /// <summary>Dice si la organización del token es una de las dadas (por ejemplo, las dos de una carga).</summary>
    public static bool PerteneceA(this ClaimsPrincipal usuario, params Guid[] organizaciones) =>
        usuario.TenantId() is { } propia && organizaciones.Contains(propia);
}

/// <summary>
/// Qué tokens acepta este módulo (sección <c>Jwt</c> de la configuración). Solo se configura quién los emite y para quién
/// son. La clave para comprobar la firma no está aquí: se descarga de Identity.
/// </summary>
public sealed class OpcionesJwt
{
    /// <summary>Quién emitió el token. Es distinto en cada entorno, así un token de desarrollo no sirve en producción.</summary>
    public string Emisor { get; set; } = string.Empty;

    /// <summary>Para quién es el token. En este módulo es «ecotrace-cargo».</summary>
    public string Audiencia { get; set; } = string.Empty;

    /// <summary>Cada cuántos minutos se vuelve a pedir la clave pública a Identity, aunque nadie lo pida.</summary>
    public int RenovarJwksCadaMinutos { get; set; } = 60;

    /// <summary>
    /// Mínimo de segundos entre dos pedidos de la clave por una clave desconocida. Sin este límite, quien mande tokens con claves
    /// inventadas haría que este módulo bombardee a Identity.
    /// </summary>
    public int MinimoEntreRenovacionesSegundos { get; set; } = 30;
}

/// <summary>Convierte a Cargo en un «portero» que revisa los tokens de Identity (ADR 0004).</summary>
public static class SeguridadRecursos
{
    /// <summary>El nombre del cliente HTTP con el que se descarga la clave pública de Identity. Las pruebas lo reemplazan.</summary>
    public const string ClienteJwks = "identity-jwks";

    /// <summary>
    /// Hace de Cargo &amp; Tracking un Resource Server (ADR 0004): revisa por su cuenta los tokens que emite Identity, sin llamarlo
    /// en cada solicitud. Comprueba, en este orden: la firma, que no haya vencido (<c>exp</c>), quién lo emitió (<c>iss</c>) y
    /// para quién es (<c>aud</c>). Si algo falla responde 401 y no intenta arreglar el token.
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

                // La clave pública de Identity se descarga una vez y se guarda en memoria. Se vuelve a pedir cada hora, o si
                // llega un token firmado con una clave desconocida (con un mínimo de tiempo entre pedidos). Nunca en cada
                // solicitud: si Identity se cae, la copia guardada sigue sirviendo y los tokens válidos siguen funcionando.
                var cliente = fabricaHttp.CreateClient(ClienteJwks);
                var direccion = new Uri(cliente.BaseAddress!, "/.well-known/jwks.json").ToString();
                opciones.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    direccion, new RecuperadorDeJwks(), new HttpDocumentRetriever(cliente) { RequireHttps = false })
                {
                    AutomaticRefreshInterval = TimeSpan.FromMinutes(configurado.RenovarJwksCadaMinutos),
                    RefreshInterval = TimeSpan.FromSeconds(configurado.MinimoEntreRenovacionesSegundos)
                };

                // Los datos del token (claims) se leen con el nombre que trae el token (sub, role, tenant_id), no con los nombres largos de Microsoft.
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

/// <summary>El traductor: convierte la lista de claves públicas que publica Identity (JWKS) en el formato que entiende el validador de tokens.</summary>
internal sealed class RecuperadorDeJwks : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    /// <summary>Descarga la lista de claves públicas de Identity y la deja lista para comprobar firmas.</summary>
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
/// Qué responde Cargo cuando un token no sirve. Al cliente le llega un mensaje <b>genérico</b>, y el motivo exacto va solo al
/// registro del servidor: así quien esté probando un ataque no aprende si falló la firma, el vencimiento o la audiencia.
/// </summary>
public static class RespuestasDeSeguridad
{
    /// <summary>
    /// Define tres reacciones: si falla la revisión del token se anota el motivo en el registro (sin el token); si falta un token
    /// válido se responde 401 «Falta un token válido»; y si el token es válido pero no tiene permiso se responde 403.
    /// </summary>
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
