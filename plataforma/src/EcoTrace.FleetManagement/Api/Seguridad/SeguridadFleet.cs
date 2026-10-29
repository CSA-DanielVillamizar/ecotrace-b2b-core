using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.FleetManagement.Api.Seguridad;

/// <summary>Lo que Fleet necesita saber para confiar en un token. Cambia por entorno (ADR 0006).</summary>
public sealed class OpcionesJwt
{
    public const string Seccion = "Jwt";

    /// <summary>Emisor esperado (<c>iss</c>). Es distinto en desarrollo y en producción.</summary>
    public string Emisor { get; set; } = string.Empty;

    /// <summary>Audiencia propia (<c>aud</c>): el token tiene que haber sido emitido para Fleet.</summary>
    public string Audiencia { get; set; } = string.Empty;

    /// <summary>Dirección del JWKS de Identity, de donde sale la clave pública.</summary>
    public string UrlJwks { get; set; } = string.Empty;
}

/// <summary>
/// Fleet Management como Resource Server (Trabajo 3, ADR 0004). Valida el JWT que emite Identity con la
/// clave pública de su JWKS: la descarga una vez, la guarda en memoria y solo la vuelve a pedir si llega
/// un <c>kid</c> que no conoce. Así no se llama a Identity en cada solicitud y, si Identity se cae, un
/// token válido se sigue aceptando.
/// </summary>
public static class SeguridadFleet
{
    /// <summary>Cliente HTTP con el que se descarga el JWKS. Las pruebas lo conectan al Identity en memoria.</summary>
    public const string ClienteJwks = "identity-jwks";

    private static readonly TimeSpan ToleranciaReloj = TimeSpan.FromSeconds(30);

    public static WebApplicationBuilder AddSeguridadFleet(this WebApplicationBuilder builder)
    {
        // Sin emisor, audiencia o JWKS el servicio no arranca: es mejor fallar al inicio que aceptar tokens a ciegas.
        builder.Services.AddOptions<OpcionesJwt>()
            .BindConfiguration(OpcionesJwt.Seccion)
            .Validate(o => !string.IsNullOrWhiteSpace(o.Emisor), "Falta Jwt:Emisor.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Audiencia), "Falta Jwt:Audiencia.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.UrlJwks), "Falta Jwt:UrlJwks.")
            .ValidateOnStart();

        builder.Services.AddHttpClient(ClienteJwks);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IHttpClientFactory, IOptions<OpcionesJwt>>((o, fabrica, opciones) =>
            {
                var jwt = opciones.Value;

                // Sin esto ASP.NET renombra "role" y "sub" a los nombres largos de WS-Federation.
                o.MapInboundClaims = false;

                // Identity no publica un documento de OpenID, solo el JWKS. RequireHttps va en false porque los
                // dos entornos de la materia corren en http local; en un despliegue real tendría que ir en true.
                o.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                    jwt.UrlJwks,
                    new JwksRetriever(),
                    new HttpDocumentRetriever(fabrica.CreateClient(ClienteJwks)) { RequireHttps = false });

                o.TokenValidationParameters = new TokenValidationParameters
                {
                    // Orden del contrato: firma, exp, iss y aud. Si cualquiera falla, 401.
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = ToleranciaReloj,
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Emisor,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audiencia,
                    NameClaimType = "sub",
                    RoleClaimType = "role"
                };

                o.Events = new JwtBearerEvents
                {
                    OnChallenge = ResponderNoAutenticadoAsync,
                    OnForbidden = contexto => EscribirProblemaAsync(
                        contexto.HttpContext, StatusCodes.Status403Forbidden, "Sin permiso",
                        "El token es válido pero no tiene permiso para esta operación.")
                };
            });

        // Un token válido sin el rol pedido recibe 403, no 401: quién es está claro, lo que no tiene es permiso.
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Politicas.GestionaFlota, p => p.RequireRole(Roles.Supervisor, Roles.Administrador))
            .AddPolicy(Politicas.Servicio, p => p.RequireRole(Roles.Servicio));
        return builder;
    }

    public static WebApplication UseSeguridadFleet(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    private static Task ResponderNoAutenticadoAsync(JwtBearerChallengeContext contexto)
    {
        contexto.HandleResponse();

        // El motivo exacto (vencido, otra firma, otro emisor...) queda en el registro del servidor.
        // Al cliente le llega siempre el mismo 401, para no darle pistas a quien prueba tokens forjados.
        var motivo = contexto.AuthenticateFailure?.GetType().Name ?? "SinToken";
        contexto.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("EcoTrace.FleetManagement.Seguridad")
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

/// <summary>Convierte el JWKS de Identity en la configuración que espera JwtBearer.</summary>
public sealed class JwksRetriever : IConfigurationRetriever<OpenIdConnectConfiguration>
{
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var configuracion = new OpenIdConnectConfiguration();
        foreach (var clave in new JsonWebKeySet(await retriever.GetDocumentAsync(address, cancel)).GetSigningKeys())
        {
            configuracion.SigningKeys.Add(clave);
        }

        return configuracion;
    }
}
