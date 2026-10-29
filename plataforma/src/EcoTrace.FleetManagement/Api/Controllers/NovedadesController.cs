using EcoTrace.FleetManagement.Api.Contracts;
using EcoTrace.FleetManagement.Api.Seguridad;
using EcoTrace.FleetManagement.Domain;
using EcoTrace.FleetManagement.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Api.Controllers;

/// <summary>
/// Novedades de un vehículo (Trabajo 3). Es la acción que la app del conductor encola sin señal y envía
/// cuando vuelve la conexión (ADR 0005).
/// </summary>
[ApiController]
[Authorize]
[Route("api/vehiculos/{vehiculoId:guid}/novedades")]
[Produces("application/json")]
public sealed class NovedadesController(FleetManagementDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>Reporta una novedad del vehículo. Solo personas del transportista dueño del vehículo.</summary>
    [HttpPost]
    [Authorize(Policy = Politicas.ReportaNovedades)]
    [ProducesResponseType<NovedadResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NovedadResponse>> Reportar(
        Guid vehiculoId, ReportarNovedadRequest solicitud, CancellationToken ct)
    {
        var userId = User.UserId();
        if (userId is null)
        {
            return Forbid();
        }

        var vehiculo = await BuscarVehiculoAsync(vehiculoId, ct);
        if (vehiculo.TenantId != User.TenantId())
        {
            return Forbid();
        }

        var novedad = Novedad.Reportar(
            vehiculo,
            userId.Value,
            solicitud.CargaId,
            solicitud.Tipo!.Value,
            solicitud.Descripcion,
            solicitud.OcurridaEn!.Value.UtcDateTime,
            reloj.GetUtcNow().UtcDateTime);

        db.Novedades.Add(novedad);
        await db.GuardarAsync("La novedad ya estaba registrada.", ct);

        return CreatedAtAction(nameof(Listar), new { vehiculoId }, NovedadResponse.De(novedad));
    }

    /// <summary>Novedades del vehículo, la más reciente primero.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NovedadResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<NovedadResponse>>> Listar(Guid vehiculoId, CancellationToken ct)
    {
        var vehiculo = await BuscarVehiculoAsync(vehiculoId, ct);
        if (!User.PuedeVer(vehiculo.TenantId))
        {
            return Forbid();
        }

        var novedades = await db.Novedades.AsNoTracking()
            .Where(n => n.VehiculoId == vehiculoId)
            .OrderByDescending(n => n.OcurridaEn)
            .ToListAsync(ct);
        return novedades.Select(NovedadResponse.De).ToList();
    }

    private async Task<Vehiculo> BuscarVehiculoAsync(Guid vehiculoId, CancellationToken ct) =>
        await db.Vehiculos.AsNoTracking().FirstOrDefaultAsync(v => v.VehiculoId == vehiculoId, ct)
            ?? throw DomainException.NotFound("No existe un vehículo con ese identificador.");
}
