using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EcoTrace.CargoTracking.Infrastructure.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EcoTrace.CargoTracking.Infrastructure;

/// <summary>
/// Publicador en segundo plano del Outbox. Toma los mensajes Pendiente cuya hora de
/// reintento llegó y los envía a Billing con reintentos y espera creciente.
/// </summary>
public sealed class OutboxPublisher(
    IDbContextFactory<CargoTrackingDbContext> dbContextFactory,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuracion) : BackgroundService
{
    private const int MaxIntentos = 6;
    private const int TimeoutSegundos = 3;

    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PublicarPendientesAsync(ct);
            }
            catch (Exception)
            {
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private async Task PublicarPendientesAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var urlBilling = configuracion.GetValue("Billing:Url", "http://localhost:5104");

        var ahora = DateTime.UtcNow;
        var pendientes = await db.OutboxMessages
            .Where(m => m.Estado == "Pendiente" && m.ProximoIntentoEn <= ahora)
            .OrderBy(m => m.ProximoIntentoEn)
            .ToListAsync(ct);

        foreach (var mensaje in pendientes)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            var cliente = httpClientFactory.CreateClient();
            cliente.Timeout = TimeSpan.FromSeconds(TimeoutSegundos);

            var request = new EntregaConfirmadaRequest(
                mensaje.EventId,
                mensaje.EventType,
                mensaje.OccurredAt,
                mensaje.CargaId,
                mensaje.VehiculoId,
                mensaje.ConductorId,
                mensaje.GeneradorTenantId,
                mensaje.TransportistaTenantId,
                mensaje.CorrelationId);

            HttpResponseMessage? respuesta = null;
            string? error = null;

            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{urlBilling}/api/eventos/entrega-confirmada");
                httpRequest.Content = JsonContent.Create(request, options: OpcionesJson);
                httpRequest.Headers.Add("X-Correlation-Id", mensaje.CorrelationId);
                httpRequest.Headers.Add("Idempotency-Key", mensaje.EventId);

                respuesta = await cliente.SendAsync(httpRequest, ct);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                error = "Tiempo agotado (3s)";
            }
            catch (HttpRequestException ex)
            {
                error = $"Error de red: {ex.Message}";
            }
            catch (Exception ex)
            {
                error = $"Error inesperado: {ex.Message}";
            }

            if (respuesta is not null && (int)respuesta.StatusCode < 300)
            {
                mensaje.MarcarPublicado();
            }
            else if (respuesta is not null && EsPermanente(respuesta.StatusCode))
            {
                var detalle = error ?? $"HTTP {(int)respuesta.StatusCode}";
                mensaje.MarcarMuerto(detalle);
            }
            else
            {
                var detalle = error ?? $"HTTP {(int)(respuesta?.StatusCode ?? 0)}";
                var proximoIntento = CalcularProximoIntento(mensaje.Intentos + 1);
                mensaje.RegistrarFalloTransitorio(detalle, proximoIntento);

                if (mensaje.Intentos >= MaxIntentos)
                {
                    mensaje.MarcarMuerto(detalle);
                }
            }

            await db.SaveChangesAsync(ct);
        }
    }

    private static bool EsPermanente(HttpStatusCode statusCode) =>
        (int)statusCode is >= 400 and < 500 and not (408 or 429);

    private static DateTime CalcularProximoIntento(int intento)
    {
        var segundos = Math.Pow(2, intento);
        return DateTime.UtcNow.AddSeconds(segundos);
    }
}
