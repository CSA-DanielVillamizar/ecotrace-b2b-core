using EcoTrace.FleetManagement.Api.Contracts;
using EcoTrace.FleetManagement.Api.Extensions;
using EcoTrace.FleetManagement.Domain;
using EcoTrace.FleetManagement.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Api.Controllers;

/// <summary>
/// Comandos que otros contextos le piden a Fleet Management (ADR 0003). Los dos son idempotentes:
/// repetir una llamada con los mismos identificadores devuelve 200 y no cambia nada, que es lo que
/// permite a un orquestador reintentar sin duplicar el efecto.
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class RecursosController(
    FleetManagementDbContext db, SimulacionFallos simulacion) : ControllerBase
{
    /// <summary>Reserva vehículo y conductor para una carga. Los dos cambian juntos o ninguno cambia.</summary>
    [HttpPost("api/reservas")]
    [ProducesResponseType<RecursosDeCargaResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<RecursosDeCargaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RecursosDeCargaResponse>> Reservar(RecursosDeCargaRequest solicitud, CancellationToken ct)
    {
        var (vehiculo, conductor, cambio) = await AplicarAsync(
            solicitud, (v, c, cargaId) => (v.Reservar(cargaId), c.Reservar(cargaId)),
            "Otra solicitud reservó el mismo recurso. Consulte su estado y reintente.", ct);

        var respuesta = Resultado(solicitud, vehiculo, conductor, cambio);
        return cambio ? StatusCode(StatusCodes.Status201Created, respuesta) : Ok(respuesta);
    }

    /// <summary>Libera vehículo y conductor de una carga. Si ya estaban disponibles, responde 200 sin cambios.</summary>
    [HttpPost("api/liberaciones")]
    [ProducesResponseType<RecursosDeCargaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RecursosDeCargaResponse>> Liberar(RecursosDeCargaRequest solicitud, CancellationToken ct)
    {
        if (simulacion.ConsumirFallo("liberaciones"))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Servicio no disponible",
                Detail = "Fallo simulado para probar la compensación del Saga."
            });
        }

        var (vehiculo, conductor, cambio) = await AplicarAsync(
            solicitud, (v, c, cargaId) => (v.Liberar(cargaId), c.Liberar(cargaId)),
            "Otra solicitud modificó el mismo recurso. Consulte su estado y reintente.", ct);

        return Ok(Resultado(solicitud, vehiculo, conductor, cambio));
    }

    /// <summary>
    /// Aplica la operación y la guarda. Si una solicitud simultánea se adelantó (token de concurrencia),
    /// se relee y se repite una vez: si la otra hizo lo mismo para la misma carga, la operación ya no cambia
    /// nada y responde 200; si la otra tomó el recurso para una carga distinta, la repetición lanza el 409.
    /// </summary>
    private async Task<(Vehiculo Vehiculo, Conductor Conductor, bool Cambio)> AplicarAsync(
        RecursosDeCargaRequest solicitud,
        Func<Vehiculo, Conductor, Guid, (bool Vehiculo, bool Conductor)> operacion,
        string mensajeSiConflicto, CancellationToken ct)
    {
        var cargaId = solicitud.CargaId!.Value;
        for (var intento = 1; ; intento++)
        {
            var (vehiculo, conductor) = await CargarAsync(solicitud, ct);
            var (cambioVehiculo, cambioConductor) = operacion(vehiculo, conductor, cargaId);
            var cambio = cambioVehiculo || cambioConductor;
            if (!cambio)
            {
                return (vehiculo, conductor, false);
            }

            try
            {
                await db.GuardarAsync(mensajeSiConflicto, ct);
                return (vehiculo, conductor, true);
            }
            catch (DomainException conflicto) when (conflicto.Kind == DomainErrorKind.Conflict && intento == 1)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<(Vehiculo Vehiculo, Conductor Conductor)> CargarAsync(
        RecursosDeCargaRequest solicitud, CancellationToken ct)
    {
        var vehiculo = await db.Vehiculos.FirstOrDefaultAsync(v => v.VehiculoId == solicitud.VehiculoId, ct)
            ?? throw DomainException.NotFound("No existe un vehículo con ese identificador.");
        var conductor = await db.Conductores.FirstOrDefaultAsync(c => c.ConductorId == solicitud.ConductorId, ct)
            ?? throw DomainException.NotFound("No existe un conductor con ese identificador.");

        if (vehiculo.TenantId != conductor.TenantId)
        {
            throw DomainException.Validation("El vehículo y el conductor deben ser del mismo transportista.");
        }

        return (vehiculo, conductor);
    }

    private static RecursosDeCargaResponse Resultado(
        RecursosDeCargaRequest solicitud, Vehiculo vehiculo, Conductor conductor, bool cambio) =>
        new(solicitud.CargaId!.Value, vehiculo.VehiculoId, conductor.ConductorId,
            vehiculo.Estado, conductor.Estado, cambio);
}
