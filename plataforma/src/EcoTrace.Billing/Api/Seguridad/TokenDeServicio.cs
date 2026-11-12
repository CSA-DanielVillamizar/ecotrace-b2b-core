using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EcoTrace.Billing.Api.Seguridad;

/// <summary>Configuración de credenciales de cliente para este servicio ante Identity.</summary>
public sealed class OpcionesClienteServicio
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>
/// Obtiene y cachea el token de servicio (role=Servicio) para llamadas salientes hacia otros módulos (ADR 0004).
/// Usa SemaphoreSlim para concurrencia y renueva antes de vencer.
/// </summary>
public sealed class ProveedorDeTokenDeServicio(
    IHttpClientFactory fabrica,
    IOptions<OpcionesClienteServicio> opciones,
    TimeProvider reloj,
    ILogger<ProveedorDeTokenDeServicio> registro)
{
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
                registro.LogError("Falta Identity:ClientId o Identity:ClientSecret: este servicio no puede identificarse ante Identity.");
                throw new HttpRequestException("Este servicio no tiene credenciales para pedir un token de servicio.");
            }

            using var respuesta = await fabrica.CreateClient(ClienteToken).PostAsJsonAsync(
                "api/auth/token-servicio",
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

/// <summary>DelegatingHandler que inyecta automáticamente el token de servicio Bearer en cada llamada saliente.</summary>
public sealed class TokenDeServicioHandler(ProveedorDeTokenDeServicio proveedor, string audiencia) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await proveedor.ObtenerAsync(audiencia, ct));
        return await base.SendAsync(solicitud, ct);
    }
}

public static class TokenDeServicioExtensiones
{
    public static WebApplicationBuilder AddTokenDeServicio(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<OpcionesClienteServicio>(builder.Configuration.GetSection("Identity"));
        builder.Services.AddSingleton<ProveedorDeTokenDeServicio>();
        builder.Services.AddHttpClient(ProveedorDeTokenDeServicio.ClienteToken, (sp, c) =>
        {
            var direccion = sp.GetRequiredService<IConfiguration>()["Servicios:Identity"] ?? "http://localhost:5101";
            c.BaseAddress = new Uri(direccion.TrimEnd('/') + "/");
            c.Timeout = TimeSpan.FromSeconds(5);
        });
        return builder;
    }

    public static IHttpClientBuilder ConTokenDeServicio(this IHttpClientBuilder cliente, string audiencia) =>
        cliente.AddHttpMessageHandler(sp => new TokenDeServicioHandler(sp.GetRequiredService<ProveedorDeTokenDeServicio>(), audiencia));
}

