using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EcoTrace.CargoTracking.Api.Security;

/// <summary>Quién es este servicio ante Identity (sección <c>Servicio</c>). El secreto sale de configuración, nunca del repositorio.</summary>
public sealed class OpcionesClienteServicio
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>
/// Consigue y guarda el token de servicio con el que este módulo llama a otro (ADR 0004). Pide uno por módulo de destino,
/// lo reutiliza hasta que le quede poco y pide otro. Si Identity no lo entrega, la llamada falla: sin identidad no se llama,
/// y para el Saga eso es fallar cerrado.
/// </summary>
public sealed class ProveedorDeTokenDeServicio(
    IHttpClientFactory fabrica, IOptions<OpcionesClienteServicio> opciones, TimeProvider reloj, ILogger<ProveedorDeTokenDeServicio> registro)
{
    /// <summary>Nombre del cliente HTTP hacia Identity con el que se piden los tokens. Las pruebas lo reemplazan.</summary>
    public const string ClienteToken = "identity-token";

    private static readonly TimeSpan MargenAntesDeVencer = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, (string Token, DateTime ExpiraUtc)> _guardados = new();
    private readonly SemaphoreSlim _candado = new(1, 1);

    public async Task<string> ObtenerAsync(string audiencia, CancellationToken ct)
    {
        if (Vigente(audiencia) is { } actual)
        {
            return actual;
        }

        await _candado.WaitAsync(ct);
        try
        {
            if (Vigente(audiencia) is { } recienObtenido)
            {
                return recienObtenido;
            }

            var cliente = opciones.Value;
            if (string.IsNullOrWhiteSpace(cliente.ClientId) || string.IsNullOrWhiteSpace(cliente.ClientSecret))
            {
                registro.LogError("Falta Servicio:ClientId o Servicio:ClientSecret: este servicio no puede identificarse ante Identity.");
                throw new HttpRequestException("Este servicio no tiene credenciales para pedir un token de servicio.");
            }

            using var respuesta = await fabrica.CreateClient(ClienteToken).PostAsJsonAsync(
                "/api/auth/token-servicio",
                new { clientId = cliente.ClientId, clientSecret = cliente.ClientSecret, audiencia }, ct);

            if (!respuesta.IsSuccessStatusCode)
            {
                registro.LogError("Identity no entregó el token de servicio para {Audiencia} ({Estado})", audiencia, (int)respuesta.StatusCode);
                throw new HttpRequestException($"Identity no entregó el token de servicio ({(int)respuesta.StatusCode}).");
            }

            using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync(ct));
            var token = documento.RootElement.GetProperty("accessToken").GetString()!;
            var expira = documento.RootElement.GetProperty("expiraEn").GetDateTime().ToUniversalTime();
            _guardados[audiencia] = (token, expira);
            return token;
        }
        finally
        {
            _candado.Release();
        }
    }

    private string? Vigente(string audiencia) =>
        _guardados.TryGetValue(audiencia, out var guardado) && guardado.ExpiraUtc - reloj.GetUtcNow().UtcDateTime > MargenAntesDeVencer
            ? guardado.Token
            : null;
}

/// <summary>Agrega el token de servicio a cada llamada saliente hacia un módulo.</summary>
public sealed class TokenDeServicioHandler(ProveedorDeTokenDeServicio proveedor, string audiencia) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        solicitud.Headers.Authorization = new("Bearer", await proveedor.ObtenerAsync(audiencia, ct));
        return await base.SendAsync(solicitud, ct);
    }
}

public static class TokenDeServicioExtensiones
{
    /// <summary>Registra el proveedor de tokens de servicio y el cliente HTTP hacia Identity con el que los pide.</summary>
    public static WebApplicationBuilder AddTokenDeServicio(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<OpcionesClienteServicio>(builder.Configuration.GetSection("Servicio"));
        builder.Services.AddSingleton<ProveedorDeTokenDeServicio>();
        builder.Services.AddHttpClient(ProveedorDeTokenDeServicio.ClienteToken, (sp, c) =>
        {
            c.BaseAddress = new Uri(sp.GetRequiredService<IConfiguration>()["Servicios:Identity:BaseUrl"] ?? "http://localhost:5101");
            c.Timeout = TimeSpan.FromSeconds(5);
        });
        return builder;
    }

    /// <summary>Hace que las llamadas de este cliente HTTP lleven un token de servicio para el módulo de destino.</summary>
    public static IHttpClientBuilder ConTokenDeServicio(this IHttpClientBuilder cliente, string audiencia) =>
        cliente.AddHttpMessageHandler(sp => new TokenDeServicioHandler(sp.GetRequiredService<ProveedorDeTokenDeServicio>(), audiencia));
}
