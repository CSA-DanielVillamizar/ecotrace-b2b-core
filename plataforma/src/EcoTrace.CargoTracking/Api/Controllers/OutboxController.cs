using EcoTrace.CargoTracking.Domain;
using EcoTrace.CargoTracking.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.CargoTracking.Api.Controllers;

[ApiController]
[Route("api/outbox")]
[Produces("application/json")]
public sealed class OutboxController(CargoTrackingDbContext db) : ControllerBase
{
    /// <summary>Lista los mensajes del Outbox, opcionalmente filtrados por estado.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<OutboxResponse>> Listar(
        [FromQuery] string? estado, CancellationToken ct)
    {
        var consulta = db.OutboxMessages.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(estado))
        {
            consulta = consulta.Where(m => m.Estado == estado);
        }

        var mensajes = await consulta
            .OrderByDescending(m => m.CreadoEn)
            .ToListAsync(ct);

        return mensajes.Select(m => new OutboxResponse(
            m.EventId,
            m.EventType,
            m.CargaId,
            m.Estado,
            m.Intentos,
            m.UltimoError,
            m.ProximoIntentoEn)).ToList();
    }

    /// <summary>Devuelve un mensaje Muerto a Pendiente con los intentos en cero.</summary>
    [HttpPost("{eventoId}/reprocesar")]
    [ProducesResponseType<OutboxResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OutboxResponse>> Reprocesar(string eventoId, CancellationToken ct)
    {
        var mensaje = await db.OutboxMessages.FirstOrDefaultAsync(m => m.EventId == eventoId, ct);
        if (mensaje is null)
        {
            return NotFound();
        }

        if (mensaje.Estado != "Muerto")
        {
            return Conflict(new { detail = "Solo se pueden reprocesar mensajes Muertos." });
        }

        mensaje.Reprocesar();
        await db.SaveChangesAsync(ct);

        return Ok(new OutboxResponse(
            mensaje.EventId,
            mensaje.EventType,
            mensaje.CargaId,
            mensaje.Estado,
            mensaje.Intentos,
            mensaje.UltimoError,
            mensaje.ProximoIntentoEn));
    }
}

public sealed record OutboxResponse(
    string EventId,
    string EventType,
    Guid CargaId,
    string Estado,
    int Intentos,
    string? UltimoError,
    DateTime? ProximoIntentoEn);
