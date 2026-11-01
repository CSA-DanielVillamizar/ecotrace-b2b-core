using System.Text;
using EcoTrace.CargoTracking.Api.Extensions;
using EcoTrace.CargoTracking.Domain;
using EcoTrace.CargoTracking.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.CargoTracking.Api.Outbox;

/// <summary>
/// Entrega a Billing los mensajes del Outbox (ADR 0002, sección 3). La entrega es "al menos una
/// vez": si el proceso cae entre el envío y la marca de Publicado, el mensaje se reenvía, y por eso
/// el consumidor deduplica por eventId. Un fallo transitorio agenda otro intento con espera
/// exponencial; uno permanente, o agotar los intentos, deja el mensaje Muerto y genera una alerta.
/// </summary>
public sealed class OutboxPublicador(
    IServiceScopeFactory alcances,
    IHttpClientFactory clientes,
    TimeProvider reloj,
    IConfiguration configuracion,
    ILogger<OutboxPublicador> registro)
{
    public const string ClienteBilling = "billing";
    private const int LoteMaximo = 20;

    /// <summary>Intenta entregar los mensajes cuya hora de reintento ya llegó. Devuelve cuántos se publicaron.</summary>
    public async Task<int> PublicarPendientesAsync(CancellationToken ct = default)
    {
        var maximoIntentos = configuracion.GetValue("Outbox:MaximoIntentos", 6);
        var esperaBase = TimeSpan.FromSeconds(configuracion.GetValue("Outbox:EsperaBaseSegundos", 2.0));

        await using var alcance = alcances.CreateAsyncScope();
        var db = alcance.ServiceProvider.GetRequiredService<CargoTrackingDbContext>();

        var ahora = reloj.GetUtcNow().UtcDateTime;
        var pendientes = await db.Outbox
            .Where(m => m.Estado == EstadoOutbox.Pendiente && m.ProximoIntentoEn <= ahora)
            .OrderBy(m => m.CreadoEn)
            .Take(LoteMaximo)
            .ToListAsync(ct);

        var publicados = 0;
        foreach (var mensaje in pendientes)
        {
            var (exito, permanente, error) = await EnviarAsync(mensaje, ct);
            var momento = reloj.GetUtcNow().UtcDateTime;

            if (exito)
            {
                mensaje.MarcarPublicado(momento);
                publicados++;
                registro.LogInformation(
                    "Outbox: {Tipo} {EventoId} publicado (correlación {CorrelationId})",
                    mensaje.Tipo, mensaje.EventoId, mensaje.CorrelationId);
            }
            else
            {
                mensaje.RegistrarFallo(error, permanente, maximoIntentos, esperaBase, momento);
                if (mensaje.Estado == EstadoOutbox.Muerto)
                {
                    registro.LogError(
                        "ALERTA Outbox: {Tipo} {EventoId} pasó a la cola de mensajes muertos tras {Intentos} intento(s): {Error}",
                        mensaje.Tipo, mensaje.EventoId, mensaje.Intentos, error);
                }
                else
                {
                    registro.LogWarning(
                        "Outbox: {Tipo} {EventoId} no se pudo entregar (intento {Intentos}); reintento a las {Proximo:HH:mm:ss}: {Error}",
                        mensaje.Tipo, mensaje.EventoId, mensaje.Intentos, mensaje.ProximoIntentoEn, error);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        return publicados;
    }

    private async Task<(bool Exito, bool Permanente, string Error)> EnviarAsync(OutboxMensaje mensaje, CancellationToken ct)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, "api/eventos/entrega-confirmada")
        {
            Content = new StringContent(mensaje.Contenido, Encoding.UTF8, "application/json")
        };
        solicitud.Headers.Add(ApiDefaults.EncabezadoCorrelacion, mensaje.CorrelationId);
        solicitud.Headers.Add("Idempotency-Key", mensaje.EventoId.ToString());

        try
        {
            var cliente = clientes.CreateClient(ClienteBilling);
            using var respuesta = await cliente.SendAsync(solicitud, ct);
            if (respuesta.IsSuccessStatusCode)
            {
                return (true, false, string.Empty);
            }

            var estado = (int)respuesta.StatusCode;
            var detalle = await respuesta.Content.ReadAsStringAsync(ct);

            // Un 4xx (salvo 408 y 429) significa que el consumidor entendió el mensaje y lo rechazó:
            // repetirlo no lo va a arreglar. Un 5xx o un tiempo agotado sí puede resolverse solo.
            var permanente = estado is >= 400 and < 500 and not 408 and not 429;
            return (false, permanente, $"HTTP {estado}: {detalle}");
        }
        catch (HttpRequestException ex)
        {
            return (false, false, $"Sin conexión con Billing: {ex.Message}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, false, "Billing no respondió a tiempo.");
        }
    }
}
