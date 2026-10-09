using EcoTrace.Billing.Api.Contracts;
using EcoTrace.Billing.Api.Extensions;
using EcoTrace.Billing.Domain;
using EcoTrace.Billing.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Api.Controllers;

/// <summary>
/// Consumidor de los eventos que otros contextos le publican a Billing (ADR 0002). Responde 202 cuando
/// el evento quedó aceptado y guardado; eso no significa que el dinero ya se haya movido, porque el
/// Saga se ejecuta después. Es idempotente: un eventId repetido, o un segundo evento para un pago que
/// ya tiene Saga, responde 200 y no crea nada nuevo.
/// </summary>
[ApiController]
[Route("api/eventos")]
[Produces("application/json")]
public sealed class EventosController(BillingDbContext db, TimeProvider reloj) : ControllerBase
{
    [HttpPost("entrega-confirmada")]
    [ProducesResponseType<EventoProcesadoResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<EventoProcesadoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EventoProcesadoResponse>> EntregaConfirmada(
        EntregaConfirmadaRequest evento, CancellationToken ct)
    {
        const string tipo = "EntregaConfirmada";
        var eventoId = evento.EventId!.Value;

        if (evento.EventType is not null && evento.EventType != tipo)
        {
            throw DomainException.Validation($"Este endpoint solo recibe eventos {tipo}.");
        }

        // 1. Ya procesado: se reconoce y no se hace nada más.
        if (await db.EventosRecibidos.AnyAsync(e => e.EventoId == eventoId, ct))
        {
            return Ok(new EventoProcesadoResponse("duplicado", null, "El evento ya había sido procesado."));
        }

        // 2. Debe existir el pago de esa carga, con las mismas organizaciones y aún en custodia.
        var pago = await db.Pagos.FirstOrDefaultAsync(p => p.CargaId == evento.CargaId, ct)
            ?? throw DomainException.NotFound("No existe un pago en Escrow para esa carga.");

        if ((evento.GeneradorTenantId is { } generador && generador != pago.GeneradorTenantId)
            || (evento.TransportistaTenantId is { } transportista && transportista != pago.TransportistaTenantId))
        {
            throw DomainException.Conflict("Las organizaciones del evento no coinciden con las del pago.");
        }

        var ahora = reloj.GetUtcNow().UtcDateTime;

        // 3. Otro evento ya inició el Saga de este pago: la clave de negocio es el pago, no el evento.
        if (await db.Sagas.AnyAsync(s => s.PagoId == pago.PagoId, ct))
        {
            db.EventosRecibidos.Add(EventoRecibido.Registrar(eventoId, tipo, ahora));
            await GuardarEventoAsync(eventoId, pago.PagoId, ct);
            return Ok(new EventoProcesadoResponse("duplicado", null, "Ya existe un Saga de liberación para este pago."));
        }

        var saga = SagaLiberacionPago.Iniciar(
            pago, evento.VehiculoId!.Value, evento.ConductorId!.Value, eventoId.ToString(), CorrelationDe(evento), ahora);

        db.EventosRecibidos.Add(EventoRecibido.Registrar(eventoId, tipo, ahora));
        db.Sagas.Add(saga);

        if (!await GuardarEventoAsync(eventoId, pago.PagoId, ct))
        {
            return Ok(new EventoProcesadoResponse("duplicado", null, "Otra entrega ganó la carrera y ya inició el Saga de este pago."));
        }

        return Accepted($"/api/sagas/{saga.SagaId}", new EventoProcesadoResponse("aceptado", saga.SagaId, null));
    }

    /// <summary>
    /// Guarda el evento recibido (y el Saga, si se agregó). Devuelve false si una entrega simultánea se
    /// adelantó: el mismo evento (clave única por eventId) o uno distinto para el mismo pago (clave única
    /// por PagoId). En los dos casos el contrato pide 200 "duplicado", no un 409 que Cargo daría por
    /// permanente y mandaría a mensajes muertos.
    /// </summary>
    private async Task<bool> GuardarEventoAsync(Guid eventoId, Guid pagoId, CancellationToken ct)
    {
        try
        {
            await db.GuardarAsync("El evento ya fue procesado.", ct);
            return true;
        }
        catch (DomainException conflicto) when (conflicto.Kind == DomainErrorKind.Conflict)
        {
            db.ChangeTracker.Clear();
            if (await db.EventosRecibidos.AnyAsync(e => e.EventoId == eventoId, ct)
                || await db.Sagas.AnyAsync(s => s.PagoId == pagoId, ct))
            {
                return false;
            }

            throw;
        }
    }

    private string CorrelationDe(EntregaConfirmadaRequest evento) =>
        !string.IsNullOrWhiteSpace(evento.CorrelationId) && evento.CorrelationId.Length <= 64
            && evento.CorrelationId.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            ? evento.CorrelationId
            : HttpContext.CorrelationId();
}
