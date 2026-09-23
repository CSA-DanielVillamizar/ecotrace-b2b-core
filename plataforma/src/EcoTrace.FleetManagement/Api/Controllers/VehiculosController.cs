using EcoTrace.FleetManagement.Api.Contracts;
using EcoTrace.FleetManagement.Domain;
using EcoTrace.FleetManagement.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Api.Controllers;

[ApiController]
[Route("api/vehiculos")]
[Produces("application/json")]
public sealed class VehiculosController(FleetManagementDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>Registra un vehículo. La placa es única en toda la plataforma.</summary>
    [HttpPost]
    [ProducesResponseType<VehiculoResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VehiculoResponse>> Crear(CrearVehiculoRequest solicitud, CancellationToken ct)
    {
        var vehiculo = Vehiculo.Crear(
            solicitud.TenantId!.Value,
            solicitud.RegistradoPorUserId!.Value,
            solicitud.Placa,
            solicitud.CapacidadKg!.Value,
            reloj.GetUtcNow().UtcDateTime);

        db.Vehiculos.Add(vehiculo);
        await db.GuardarAsync("Ya existe un vehículo con esa placa.", ct);

        return CreatedAtAction(nameof(Obtener), new { id = vehiculo.VehiculoId }, VehiculoResponse.De(vehiculo));
    }

    /// <summary>Lista los vehículos, con filtro opcional por transportista.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<VehiculoResponse>> Listar([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var consulta = db.Vehiculos.AsNoTracking();
        if (tenantId is not null)
        {
            consulta = consulta.Where(v => v.TenantId == tenantId);
        }

        var vehiculos = await consulta.OrderBy(v => v.Placa).ToListAsync(ct);
        return vehiculos.Select(VehiculoResponse.De).ToList();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<VehiculoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VehiculoResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var vehiculo = await db.Vehiculos.AsNoTracking().FirstOrDefaultAsync(v => v.VehiculoId == id, ct)
            ?? throw DomainException.NotFound("No existe un vehículo con ese identificador.");

        return VehiculoResponse.De(vehiculo);
    }
}
