using EcoTrace.CargoTracking.Api.Contracts;
using EcoTrace.CargoTracking.Api.Security;
using EcoTrace.CargoTracking.Domain;
using EcoTrace.CargoTracking.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.CargoTracking.Api.Controllers;

/// <summary>
/// Muestra y administra el Outbox: la bandeja donde Cargo guarda los mensajes que todavía tiene que enviar a Billing
/// (por ejemplo «se entregó la carga X»). Es la ruta de reproceso que el ADR 0002 pide junto a la cola de mensajes muertos.
/// <para>
/// Qué se puede hacer: ver los mensajes (pendientes, publicados o muertos) y devolver un mensaje muerto a pendiente para
/// que se intente enviar de nuevo.
/// </para>
/// <para>
/// Quién puede usarlo: solo un servicio interno (rol <c>Servicio</c>), no un usuario. El Outbox guarda eventos de todas
/// las organizaciones, y el token de una persona no dice si opera la plataforma. Dejar que un administrador viera todo
/// rompería el aislamiento entre organizaciones. Pendiente: dar acceso a un administrador viendo solo los mensajes de su
/// organización.
/// </para>
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Servicio)]
[Route("api/outbox")]
[Produces("application/json")]
public sealed class OutboxController(CargoTrackingDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>
    /// Lista los mensajes del Outbox, del más nuevo al más viejo (máximo 100). Se puede filtrar por estado:
    /// Pendiente, Publicado o Muerto.
    /// </summary>
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

    /// <summary>
    /// Devuelve un mensaje muerto (que agotó sus intentos) a Pendiente, para que el publicador lo intente enviar otra vez.
    /// Si el mensaje no existe responde 404, y si no está muerto responde 409.
    /// </summary>
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
