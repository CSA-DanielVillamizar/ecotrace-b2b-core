using EcoTrace.FleetManagement.Api.Contracts;
using EcoTrace.FleetManagement.Api.Seguridad;
using EcoTrace.FleetManagement.Domain;
using EcoTrace.FleetManagement.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/vehiculos")]
[Produces("application/json")]
public sealed class VehiculosController(FleetManagementDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>
    /// Registra un vehículo en la organización del token. La placa es única en toda la plataforma.
    /// Solo un Supervisor o un Administrador del transportista puede hacerlo.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Politicas.GestionaFlota)]
    [ProducesResponseType<VehiculoResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VehiculoResponse>> Crear(CrearVehiculoRequest solicitud, CancellationToken ct)
    {
        // La organización y quién registra salen del token: así nadie registra flota a nombre de otra organización.
        var tenantId = User.TenantId();
        var userId = User.UserId();
        if (tenantId is null || userId is null)
        {
            return Forbid();
        }

        var vehiculo = Vehiculo.Crear(
            tenantId.Value,
            userId.Value,
            solicitud.Placa,
            solicitud.CapacidadKg!.Value,
            reloj.GetUtcNow().UtcDateTime);

        db.Vehiculos.Add(vehiculo);
        await db.GuardarAsync("Ya existe un vehículo con esa placa.", ct);

        return CreatedAtAction(nameof(Obtener), new { id = vehiculo.VehiculoId }, VehiculoResponse.De(vehiculo));
    }

    /// <summary>Lista los vehículos de la organización del token. Un servicio los ve todos.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<VehiculoResponse>> Listar(CancellationToken ct)
    {
        var consulta = db.Vehiculos.AsNoTracking();
        if (!User.EsServicio())
        {
            var tenantId = User.TenantId();
            consulta = consulta.Where(v => v.TenantId == tenantId);
        }

        var vehiculos = await consulta.OrderBy(v => v.Placa).ToListAsync(ct);
        return vehiculos.Select(VehiculoResponse.De).ToList();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<VehiculoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VehiculoResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var vehiculo = await db.Vehiculos.AsNoTracking().FirstOrDefaultAsync(v => v.VehiculoId == id, ct)
            ?? throw DomainException.NotFound("No existe un vehículo con ese identificador.");

        if (!User.PuedeVer(vehiculo.TenantId))
        {
            return Forbid();
        }

        return VehiculoResponse.De(vehiculo);
    }
}
