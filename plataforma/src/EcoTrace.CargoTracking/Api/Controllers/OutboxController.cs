using EcoTrace.CargoTracking.Api.Contracts;
using EcoTrace.CargoTracking.Domain;
using EcoTrace.CargoTracking.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.CargoTracking.Api.Controllers;

/// <summary>
/// Vista operativa del Outbox: qué mensajes esperan, cuáles se publicaron y cuáles quedaron en la
/// cola de mensajes muertos. Es el procedimiento de reproceso que el ADR 0002 exige junto a la DLQ.
/// </summary>
[ApiController]
[Route("api/outbox")]
[Produces("application/json")]
public sealed class OutboxController(CargoTrackingDbContext db, TimeProvider reloj) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<OutboxMensajeResponse>> Listar([FromQuery] EstadoOutbox? estado, CancellationToken ct)
    {
        var consulta = db.Outbox.AsNoTracking();
        if (estado is not null)
        {
            consulta = consulta.Where(m => m.Estado == estado);
        }

        var mensajes = await consulta.OrderByDescending(m => m.CreadoEn).Take(100).ToListAsync(ct);
        return mensajes.Select(OutboxMensajeResponse.De).ToList();
    }

    /// <summary>Devuelve un mensaje muerto a Pendiente para que el publicador lo intente de nuevo.</summary>
    [HttpPost("{eventoId:guid}/reprocesar")]
    [ProducesResponseType<OutboxMensajeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OutboxMensajeResponse>> Reprocesar(Guid eventoId, CancellationToken ct)
    {
        var mensaje = await db.Outbox.FirstOrDefaultAsync(m => m.EventoId == eventoId, ct)
            ?? throw DomainException.NotFound("No existe un mensaje con ese identificador.");

        mensaje.Reprocesar(reloj.GetUtcNow().UtcDateTime);
        await db.GuardarAsync("No se pudo reprocesar el mensaje.", ct);

        return OutboxMensajeResponse.De(mensaje);
    }
}
