using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EcoTrace.CargoTracking.Api.Security;

/// <summary>
/// Quién es este servicio ante Identity (sección <c>Identity</c> de la configuración). El <c>ClientId</c> puede estar en el
/// repositorio; el <c>ClientSecret</c> nunca: en desarrollo va en <c>appsettings.Development.json</c> y en producción se
/// entrega por variable de entorno (<c>Identity__ClientSecret</c>).
/// </summary>
public sealed class OpcionesClienteServicio
{
    /// <summary>El nombre con el que este servicio se presenta ante Identity (aquí, «cargo»).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>La contraseña de este servicio ante Identity. Nunca va en el repositorio, salvo la de desarrollo.</summary>
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>
/// Consigue y guarda el token con el que Cargo llama a otro módulo (ADR 0004). En el Trabajo 2 los servicios se llamaban sin
/// identidad; ahora cada llamada entre servicios lleva un token de servicio: dura pocos minutos y sirve para un solo módulo.
/// <para>
/// En palabras simples: pide un token por cada módulo de destino y lo reutiliza hasta que le queda poco para vencer, en vez de
/// pedir uno nuevo en cada llamada. Si Identity no lo entrega, la llamada no sale: <b>sin identidad no se llama</b>. Para el
/// Outbox eso significa que el mensaje queda pendiente y se reintenta, no que se pierda.
/// </para>
/// </summary>
/// <param name="fabrica">Crea los clientes HTTP.</param>
/// <param name="opciones">El nombre y la contraseña de este servicio ante Identity.</param>
/// <param name="reloj">El reloj, para saber cuánto le queda al token (las pruebas pueden cambiarlo).</param>
/// <param name="registro">Donde se anotan los problemas.</param>
public sealed class ProveedorDeTokenDeServicio(
    IHttpClientFactory fabrica, IOptions<OpcionesClienteServicio> opciones, TimeProvider reloj, ILogger<ProveedorDeTokenDeServicio> registro)
{
    /// <summary>El nombre del cliente HTTP hacia Identity con el que se piden los tokens. Las pruebas lo reemplazan.</summary>
    public const string ClienteToken = "identity-token";

    /// <summary>Si al token le queda menos que esto, se pide uno nuevo, para no mandar uno que venza en el camino.</summary>
    private static readonly TimeSpan MargenAntesDeVencer = TimeSpan.FromSeconds(60);

    /// <summary>Los tokens ya conseguidos, uno por módulo de destino, con la hora en que vencen.</summary>
    private readonly ConcurrentDictionary<string, (string Token, DateTime VenceUtc)> _guardados = new();
    /// <summary>Un candado para que solo se pida un token a la vez.</summary>
    private readonly SemaphoreSlim _candado = new(1, 1);

    /// <summary>
    /// Devuelve un token para llamar al módulo <paramref name="audiencia"/> (por ejemplo, Billing). Si ya hay uno vigente lo
    /// reutiliza; si no, se lo pide a Identity.
    /// </summary>
    /// <exception cref="HttpRequestException">Faltan las credenciales o Identity no entregó el token.</exception>
    public async Task<string> ObtenerAsync(string audiencia, CancellationToken ct)
    {
        if (Vigente(audiencia) is { } actual)
        {
            return actual;
        }

        // Un solo pedido a la vez: si llegan varias llamadas juntas sin token, solo una va a Identity y las demás reutilizan el resultado.
        await _candado.WaitAsync(ct);
        try
        {
            if (Vigente(audiencia) is { } reciente)
            {
                return reciente;
            }

            var cliente = opciones.Value;
            if (string.IsNullOrWhiteSpace(cliente.ClientId) || string.IsNullOrWhiteSpace(cliente.ClientSecret))
            {
                registro.LogError("Falta Identity:ClientId o Identity:ClientSecret: este servicio no puede identificarse ante Identity.");
                throw new HttpRequestException("Este servicio no tiene credenciales para pedir un token de servicio.");
            }

            using var respuesta = await fabrica.CreateClient(ClienteToken).PostAsJsonAsync(
                "/api/auth/token-servicio",
                new { clientId = cliente.ClientId, clientSecret = cliente.ClientSecret, audiencia }, ct);

            if (!respuesta.IsSuccessStatusCode)
            {
                // Se registra el estado, nunca el secreto ni el token.
                registro.LogError("Identity no entregó el token de servicio para {Audiencia} ({Estado})", audiencia, (int)respuesta.StatusCode);
                throw new HttpRequestException($"Identity no entregó el token de servicio ({(int)respuesta.StatusCode}).");
            }

            using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            var token = documento.RootElement.GetProperty("accessToken").GetString()!;
            var vence = documento.RootElement.GetProperty("expiraEn").GetDateTime().ToUniversalTime();
            _guardados[audiencia] = (token, vence);
            return token;
        }
        finally
        {
            _candado.Release();
        }
    }

    /// <summary>Devuelve el token guardado para ese módulo si todavía le queda tiempo, o null si hay que pedir uno nuevo.</summary>
    private string? Vigente(string audiencia) =>
        _guardados.TryGetValue(audiencia, out var guardado) && guardado.VenceUtc - reloj.GetUtcNow().UtcDateTime > MargenAntesDeVencer
            ? guardado.Token
            : null;
}

/// <summary>Un paso que se mete en cada llamada hacia otro módulo y le pone el token de servicio en el encabezado <c>Authorization</c>.</summary>
public sealed class TokenDeServicioHandler(ProveedorDeTokenDeServicio proveedor, string audiencia) : DelegatingHandler
{
    /// <summary>Consigue el token y lo agrega a la llamada antes de enviarla.</summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        solicitud.Headers.Authorization = new("Bearer", await proveedor.ObtenerAsync(audiencia, ct));
        return await base.SendAsync(solicitud, ct);
    }
}

/// <summary>Atajos para conectar el token de servicio al servicio y a los clientes HTTP.</summary>
public static class TokenDeServicioExtensiones
{
    /// <summary>Registra el proveedor de tokens de servicio y el cliente HTTP hacia Identity con el que los pide.</summary>
    public static WebApplicationBuilder AddTokenDeServicio(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<OpcionesClienteServicio>(builder.Configuration.GetSection("Identity"));
        builder.Services.AddSingleton<ProveedorDeTokenDeServicio>();
        builder.Services.AddHttpClient(ProveedorDeTokenDeServicio.ClienteToken, (proveedor, cliente) =>
        {
            cliente.BaseAddress = new Uri(proveedor.GetRequiredService<IConfiguration>()["Servicios:Identity:BaseUrl"] ?? "http://localhost:5101");
            cliente.Timeout = TimeSpan.FromSeconds(5);
        });
        return builder;
    }

    /// <summary>Hace que todas las llamadas de este cliente HTTP lleven un token de servicio para el módulo de destino.</summary>
    public static IHttpClientBuilder ConTokenDeServicio(this IHttpClientBuilder cliente, string audiencia) =>
        cliente.AddHttpMessageHandler(proveedor => new TokenDeServicioHandler(proveedor.GetRequiredService<ProveedorDeTokenDeServicio>(), audiencia));
}
