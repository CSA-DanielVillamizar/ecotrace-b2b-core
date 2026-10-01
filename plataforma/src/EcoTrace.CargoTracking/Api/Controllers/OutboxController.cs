using EcoTrace.CargoTracking.Api.Contracts;
using EcoTrace.CargoTracking.Domain;
using EcoTrace.CargoTracking.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.CargoTracking.Api.Controllers;

/// <summary>
/// Endpoints de OPERACIÓN del Outbox -- no mueven datos de negocio (no crean cargas ni
/// seguimientos), sirven para observar el mecanismo de mensajería y, si algo se atascó,
/// revivirlo manualmente. Por eso viven separados de CargasController.
/// </summary>
[ApiController]
[Route("api/outbox")]
[Produces("application/json")]
public sealed class OutboxController(CargoTrackingDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>
    /// Lista los mensajes del Outbox, opcionalmente filtrados por estado. Es el checkpoint que
    /// permite demostrar el patrón Outbox sin que Billing exista todavía: se registra una
    /// entrega, y aquí se confirma que el mensaje quedó Pendiente, listo para publicarse en
    /// cuanto Billing esté arriba.
    /// </summary>
    [HttpGet]
    public async Task<IReadOnlyList<MensajeOutboxResponse>> Listar(
        [FromQuery] EstadoMensaje? estado, CancellationToken ct)
    {
        var consulta = db.MensajesOutbox.AsNoTracking().AsQueryable();
        if (estado is not null)
        {
            consulta = consulta.Where(m => m.Estado == estado);
        }

        var mensajes = await consulta.OrderByDescending(m => m.OcurrioEn).ToListAsync(ct);
        return mensajes.Select(MensajeOutboxResponse.De).ToList();
    }

    /// <summary>
    /// Devuelve un mensaje Muerto a Pendiente, con los intentos en cero -- la "ruta de
    /// reproceso" que exige la especificación. eventoId es el EventId (string), no el Id interno
    /// del mensaje: es como la especificación identifica al mensaje ("Idempotency-Key (el
    /// eventId)" en la sección Publicador), y es coherente con que ese mismo valor viaja hacia
    /// Billing.
    /// </summary>
    [HttpPost("{eventoId}/reprocesar")]
    [ProducesResponseType<MensajeOutboxResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MensajeOutboxResponse>> Reprocesar(string eventoId, CancellationToken ct)
    {
        var mensaje = await db.MensajesOutbox.FirstOrDefaultAsync(m => m.EventId == eventoId, ct)
            ?? throw DomainException.NotFound($"No existe ningún mensaje con EventId '{eventoId}'.");

        // La validación "solo se reprocesa lo Muerto" vive en MensajeOutbox.Reprocesar
        // (lanza DomainException.Conflict si no aplica) -- este controlador no la repite,
        // solo orquesta: buscar, pedirle al dominio que aplique la regla, guardar.
        mensaje.Reprocesar(reloj.GetUtcNow().UtcDateTime);
        await db.GuardarAsync("No se pudo reprocesar el mensaje.", ct);

        return MensajeOutboxResponse.De(mensaje);
    }
}
