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
        var (vehiculo, conductor) = await CargarAsync(solicitud, ct);

        var cambioVehiculo = vehiculo.Reservar(solicitud.CargaId!.Value);
        var cambioConductor = conductor.Reservar(solicitud.CargaId!.Value);
        var cambio = cambioVehiculo || cambioConductor;
        if (cambio)
        {
            await db.GuardarAsync("Otra solicitud reservó el mismo recurso. Consulte su estado y reintente.", ct);
        }

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

        var (vehiculo, conductor) = await CargarAsync(solicitud, ct);

        var cambioVehiculo = vehiculo.Liberar(solicitud.CargaId!.Value);
        var cambioConductor = conductor.Liberar(solicitud.CargaId!.Value);
        var cambio = cambioVehiculo || cambioConductor;
        if (cambio)
        {
            await db.GuardarAsync("Otra solicitud modificó el mismo recurso. Consulte su estado y reintente.", ct);
        }

        return Ok(Resultado(solicitud, vehiculo, conductor, cambio));
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
