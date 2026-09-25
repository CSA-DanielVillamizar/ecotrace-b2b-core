using EcoTrace.FleetManagement.Api.Contracts;
using EcoTrace.FleetManagement.Api.Extensions;
using EcoTrace.FleetManagement.Domain;
using EcoTrace.FleetManagement.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Api.Controllers;

[ApiController]
[Route("api/liberaciones")]
[Produces("application/json")]
public sealed class LiberacionesController(
    FleetManagementDbContext db, SimulacionDeFallos simulacion) : ControllerBase
{
    /// <summary>
    /// Libera vehículo y conductor de una carga. Es el paso 3 del Saga "Liberar Pago en Escrow":
    /// Billing lo llama hasta cuatro veces, así que responde 200 aunque no haya nada que cambiar.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<AsignacionRecursosResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AsignacionRecursosResponse>> Liberar(
        AsignacionRecursosRequest solicitud, CancellationToken ct)
    {
        // El fallo simulado se consume antes de tocar la base de datos, para que el 503 nunca deje
        // una liberacion a medias. Solo hay fallos armados si Simulacion:Habilitada es true.
        if (simulacion.DebeFallar(SimulacionDeFallos.Liberaciones))
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "El servicio no está disponible",
                detail: "Fallo simulado en la liberación de recursos.");
        }

        var cargaId = solicitud.CargaId!.Value;
        var vehiculoId = solicitud.VehiculoId!.Value;
        var conductorId = solicitud.ConductorId!.Value;

        var vehiculo = await db.Vehiculos.FirstOrDefaultAsync(v => v.VehiculoId == vehiculoId, ct)
            ?? throw DomainException.NotFound("No existe un vehículo con ese identificador.");

        var conductor = await db.Conductores.FirstOrDefaultAsync(c => c.ConductorId == conductorId, ct)
            ?? throw DomainException.NotFound("No existe un conductor con ese identificador.");

        if (vehiculo.TenantId != conductor.TenantId)
        {
            throw DomainException.Validation("El vehículo y el conductor son de transportistas distintos.");
        }

        // Si uno de los dos esta reservado para otra carga, la excepcion sale antes del guardado y
        // no se libera el otro: o cambian los dos o no cambia ninguno.
        var cambioVehiculo = vehiculo.Liberar(cargaId);
        var cambioConductor = conductor.Liberar(cargaId);
        var cambioEstado = cambioVehiculo || cambioConductor;

        if (cambioEstado)
        {
            await db.GuardarAsync("El vehículo o el conductor están reservados para otra carga.", ct);
        }

        return Ok(AsignacionRecursosResponse.De(cargaId, vehiculo, conductor, cambioEstado));
    }
}
