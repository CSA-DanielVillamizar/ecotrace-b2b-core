using System.Net.Http.Json;
using System.Text.Json;
using EcoTrace.Billing.Api.Extensions;

namespace EcoTrace.Billing.Api.Saga;

/// <summary>
/// Resultado de una llamada a otro servicio, ya clasificado. Permanente significa que el servicio
/// entendió la solicitud y la rechazó (un 4xx): repetirla no cambia nada. Lo demás que falla
/// (red caída, tiempo agotado, 5xx, 408, 429) es transitorio y puede reintentarse.
/// </summary>
public sealed record RespuestaServicio(bool Exito, bool Permanente, int? Estado, string Detalle, JsonElement? Cuerpo)
{
    public string? Texto(string propiedad) =>
        Cuerpo is { ValueKind: JsonValueKind.Object } cuerpo && cuerpo.TryGetProperty(propiedad, out var valor)
            ? valor.GetString()
            : null;
}

/// <summary>
/// Llamadas de Billing a Identity y Fleet Management. Nunca toca sus bases de datos: solo habla con
/// sus APIs, con el contrato de cada una y con referencias por identificador. Identity lleva la
/// política del ADR 0002 (500 ms por intento, dos reintentos con 100 y 200 ms de espera); los
/// reintentos de Fleet los decide el orquestador para que queden guardados y sobrevivan a un reinicio.
/// </summary>
public sealed class ServiciosExternos(
    IHttpClientFactory clientes, IConfiguration configuracion, ILogger<ServiciosExternos> registro)
{
    public const string Identity = "Identity";
    public const string Fleet = "Fleet";

    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web);

    public Task<RespuestaServicio> AutorizarPagoAsync(Guid pagoId, Guid tenantId, string correlationId, CancellationToken ct) =>
        EnviarAsync(Identity, HttpMethod.Post, "api/autorizaciones-pago", new { pagoId, tenantId }, correlationId, ct);

    public Task<RespuestaServicio> ObtenerAutorizacionAsync(Guid pagoId, string correlationId, CancellationToken ct) =>
        EnviarAsync(Identity, HttpMethod.Get, $"api/autorizaciones-pago/{pagoId}", null, correlationId, ct);

    public Task<RespuestaServicio> RevocarAutorizacionAsync(Guid pagoId, string correlationId, CancellationToken ct) =>
        EnviarAsync(Identity, HttpMethod.Post, $"api/autorizaciones-pago/{pagoId}/revocacion", null, correlationId, ct);

    public Task<RespuestaServicio> LiberarRecursosAsync(
        Guid cargaId, Guid vehiculoId, Guid conductorId, string correlationId, CancellationToken ct) =>
        EnviarAsync(Fleet, HttpMethod.Post, "api/liberaciones", new { cargaId, vehiculoId, conductorId }, correlationId, ct);

    private async Task<RespuestaServicio> EnviarAsync(
        string servicio, HttpMethod metodo, string ruta, object? cuerpo, string correlationId, CancellationToken ct)
    {
        var porDefectoIdentity = servicio == Identity;
        var tiempo = TimeSpan.FromMilliseconds(configuracion.GetValue($"Resiliencia:{servicio}:TimeoutMs", porDefectoIdentity ? 500 : 3000));
        var reintentos = configuracion.GetValue($"Resiliencia:{servicio}:Reintentos", porDefectoIdentity ? 2 : 0);
        var esperaBase = TimeSpan.FromMilliseconds(configuracion.GetValue($"Resiliencia:{servicio}:EsperaBaseMs", 100));

        for (var intento = 0; ; intento++)
        {
            var respuesta = await IntentarAsync(servicio, metodo, ruta, cuerpo, correlationId, tiempo, ct);
            if (respuesta.Exito || respuesta.Permanente || intento >= reintentos)
            {
                return respuesta;
            }

            registro.LogWarning(
                "{Servicio} {Ruta}: intento {Intento} fallido ({Detalle}); reintento en {Espera} ms",
                servicio, ruta, intento + 1, respuesta.Detalle, (esperaBase * Math.Pow(2, intento)).TotalMilliseconds);
            await Task.Delay(esperaBase * Math.Pow(2, intento), ct);
        }
    }

    private async Task<RespuestaServicio> IntentarAsync(
        string servicio, HttpMethod metodo, string ruta, object? cuerpo, string correlationId,
        TimeSpan tiempo, CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(tiempo);

        using var solicitud = new HttpRequestMessage(metodo, ruta);
        solicitud.Headers.Add(ApiDefaults.EncabezadoCorrelacion, correlationId);
        if (cuerpo is not null)
        {
            solicitud.Content = JsonContent.Create(cuerpo, options: OpcionesJson);
        }

        try
        {
            using var respuesta = await clientes.CreateClient(servicio).SendAsync(solicitud, limite.Token);
            var texto = await respuesta.Content.ReadAsStringAsync(limite.Token);
            var json = LeerJson(texto);
            var estado = (int)respuesta.StatusCode;

            if (respuesta.IsSuccessStatusCode)
            {
                return new RespuestaServicio(true, false, estado, $"{servicio} respondió {estado}.", json);
            }

            var permanente = estado is >= 400 and < 500 and not 408 and not 429;
            var explicacion = json is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("detail", out var d)
                ? d.GetString()
                : null;
            return new RespuestaServicio(false, permanente, estado, $"{servicio} respondió {estado}: {explicacion ?? "sin detalle"}", json);
        }
        catch (HttpRequestException ex)
        {
            return new RespuestaServicio(false, false, null, $"No hay conexión con {servicio}: {ex.Message}", null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new RespuestaServicio(false, false, null, $"{servicio} no respondió en {tiempo.TotalMilliseconds:N0} ms.", null);
        }
    }

    private static JsonElement? LeerJson(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(texto);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
