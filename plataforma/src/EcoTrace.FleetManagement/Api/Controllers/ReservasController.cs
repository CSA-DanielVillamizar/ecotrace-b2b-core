using EcoTrace.FleetManagement.Api.Contracts;
using EcoTrace.FleetManagement.Domain;
using EcoTrace.FleetManagement.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Api.Controllers;

[ApiController]
[Route("api/reservas")]
[Produces("application/json")]
public sealed class ReservasController(FleetManagementDbContext db) : ControllerBase
{
    /// <summary>
    /// Reserva vehículo y conductor para una carga. Los dos cambian juntos o no cambia ninguno.
    /// Repetir la misma reserva responde 200 sin cambios, en lugar de 201.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<AsignacionRecursosResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<AsignacionRecursosResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AsignacionRecursosResponse>> Reservar(
        AsignacionRecursosRequest solicitud, CancellationToken ct)
    {
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

        // Los dos Reservar validan antes de cambiar nada: si el conductor esta tomado por otra
        // carga, la excepcion sale antes del guardado y el vehiculo no queda reservado a medias.
        var cambioVehiculo = vehiculo.Reservar(cargaId);
        var cambioConductor = conductor.Reservar(cargaId);
        var cambioEstado = cambioVehiculo || cambioConductor;

        if (cambioEstado)
        {
            // Un solo guardado es una sola transaccion: entran los dos UPDATE o no entra ninguno.
            await db.GuardarAsync("El vehículo o el conductor ya están reservados para otra carga.", ct);
        }

        var respuesta = AsignacionRecursosResponse.De(cargaId, vehiculo, conductor, cambioEstado);
        return cambioEstado
            ? StatusCode(StatusCodes.Status201Created, respuesta)
            : Ok(respuesta);
    }
}
