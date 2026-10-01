using System.Net;
using System.Net.Http.Json;
using EcoTrace.CargoTracking.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;

namespace EcoTrace.CargoTracking.Infrastructure;

/// <summary>
/// Revisa cada 1 segundo los MensajeOutbox Pendiente listos para reintentar, y los entrega a
/// Billing por HTTP (patrón Outbox, ADR 0002). Corre como <see cref="BackgroundService"/> --
/// aparte del hilo que atiende peticiones HTTP -- precisamente para que Cargo &amp; Tracking
/// nunca quede bloqueado esperando a que Billing esté disponible: registrar una entrega solo
/// implica guardar una fila local; ESTE proceso, por separado, se encarga de la parte que sí
/// puede tardar o fallar.
/// </summary>
public sealed class OutboxPublicador(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    TimeProvider reloj,
    ILogger<OutboxPublicador> logger) : BackgroundService
{
    // Espera exponencial: 2s, 4s, 8s, 16s, 32s entre intentos (especificación).
    // Cada espera es el doble de la anterior, para no bombardear a Billing con
    // reintentos pegados si está teniendo un problema pasajero.
    private static readonly int[] EsperaSegundos = { 2, 4, 8, 16, 32 };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublicarPendientesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Un error inesperado aquí NO debe matar el bucle para siempre --
                // se registra y se vuelve a intentar 1 segundo después.
                logger.LogError(ex, "Error inesperado en el publicador del Outbox.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    /// <summary>
    /// Separado de ExecuteAsync a propósito: así se puede invocar directamente desde una prueba,
    /// sin depender del bucle real de 1 segundo.
    /// </summary>
    public async Task<int> PublicarPendientesAsync(CancellationToken ct)
    {
        // Un scope de DI nuevo por ciclo: este BackgroundService vive una sola
        // vez durante toda la aplicación (singleton), pero CargoTrackingDbContext
        // está diseñado para vivir poco tiempo, una operación a la vez (scoped).
        // Mezclarlos directamente causaría errores con el tiempo -- ésta es la
        // forma estándar de .NET de resolver esa diferencia de ciclo de vida.
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CargoTrackingDbContext>();
        var cliente = httpClientFactory.CreateClient("Billing");

        // Solo mensajes Pendiente CUYA hora de reintento ya llegó -- así se
        // respeta la espera creciente: un mensaje que falló hace 3 segundos y
        // debe esperar 8, simplemente no aparece todavía en esta consulta.
        var ahora = reloj.GetUtcNow().UtcDateTime;
        var pendientes = await db.MensajesOutbox
            .Where(m => m.Estado == EstadoMensaje.Pendiente && m.ProximoIntentoEn <= ahora)
            .ToListAsync(ct);

        int publicados = 0;

        foreach (var mensaje in pendientes)
        {
            // Cuerpo exacto que la especificación dice que Billing espera en
            // POST /api/eventos/entrega-confirmada.
            var cuerpo = new
            {
                eventId = mensaje.EventId,
                eventType = "EntregaConfirmada",
                occurredAt = mensaje.OcurrioEn,
                cargaId = mensaje.CargaId,
                vehiculoId = mensaje.VehiculoId,
                conductorId = mensaje.ConductorId,
                generadorTenantId = mensaje.GeneradorTenantId,
                transportistaTenantId = mensaje.TransportistaTenantId,
                correlationId = mensaje.CorrelationId
            };

            using var peticion = new HttpRequestMessage(HttpMethod.Post, "/api/eventos/entrega-confirmada")
            {
                Content = JsonContent.Create(cuerpo)
            };

            // X-Correlation-Id: para rastrear este mensaje de punta a punta en
            // los registros de los cuatro servicios.
            peticion.Headers.Add("X-Correlation-Id", mensaje.CorrelationId);

            // Idempotency-Key: la especificación pide mandar el eventId aquí,
            // para que Billing reconozca si ya procesó este mismo mensaje antes
            // (por ejemplo, tras una caída a mitad de camino) y no lo repita.
            peticion.Headers.Add("Idempotency-Key", mensaje.EventId);

            try
            {
                // Timeout de 3 segundos por intento (especificación).
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(3));

                var respuesta = await cliente.SendAsync(peticion, cts.Token);

                if ((int)respuesta.StatusCode is >= 200 and < 300)
                {
                    mensaje.MarcarPublicado();
                    publicados++;
                }
                else if (EsFalloTransitorio(respuesta.StatusCode))
                {
                    // 5xx, 408 o 429: el problema es probablemente pasajero.
                    var proximoIntento = CalcularProximoIntento(mensaje.Intentos, ahora);
                    mensaje.RegistrarReintento($"HTTP {(int)respuesta.StatusCode}", proximoIntento);
                    if (mensaje.Estado == EstadoMensaje.Muerto)
                    {
                        logger.LogWarning("{Error}", mensaje.UltimoError);
                    }
                }
                else
                {
                    // Cualquier otro 4xx es PERMANENTE (especificación: "los 4xx
                    // son permanentes para el publicador de Cargo"). Reintentar
                    // un 404 o un 409 no va a cambiar la respuesta.
                    mensaje.MarcarMuertoPermanente($"Billing respondió {(int)respuesta.StatusCode}");
                    logger.LogWarning("{Error}", mensaje.UltimoError);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // HttpRequestException: no se pudo ni conectar (Billing apagado,
                // red caída). TaskCanceledException: se agotaron los 3 segundos
                // sin respuesta. Ambos son TRANSITORIOS -- Billing puede estar
                // arriba en el próximo intento.
                var proximoIntento = CalcularProximoIntento(mensaje.Intentos, ahora);
                mensaje.RegistrarReintento(ex.Message, proximoIntento);
                if (mensaje.Estado == EstadoMensaje.Muerto)
                {
                    logger.LogWarning("{Error}", mensaje.UltimoError);
                }
            }

            // Se guarda ESTE mensaje antes de seguir con el siguiente: si el
            // proceso se cae a mitad del foreach, lo ya procesado no se pierde
            // ni se reintenta de más.
            await db.GuardarAsync("No se pudo actualizar el mensaje del Outbox.", ct);
        }

        return publicados;
    }

    // Clasifica un código HTTP como "vale la pena reintentar" o no, siguiendo
    // exactamente la regla de la especificación.
    private static bool EsFalloTransitorio(HttpStatusCode status)
    {
        var codigo = (int)status;
        return codigo == 408 || codigo == 429 || codigo >= 500;
    }

    // Calcula cuándo debería ser el próximo intento, según cuántas veces ya
    // falló este mensaje (espera creciente: 2s, 4s, 8s, 16s, 32s).
    private static DateTime CalcularProximoIntento(int intentosPrevios, DateTime ahora)
    {
        var indice = Math.Min(intentosPrevios, EsperaSegundos.Length - 1);
        return ahora.AddSeconds(EsperaSegundos[indice]);
    }
}
