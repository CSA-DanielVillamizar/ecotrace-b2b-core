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
[Route("api/conductores")]
[Produces("application/json")]
public sealed class ConductoresController(FleetManagementDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>
    /// Registra un conductor en la organización del token. Solo un Supervisor o un Administrador del
    /// transportista puede hacerlo. El UserId del conductor no se valida contra Identity: entre contextos
    /// solo viaja el identificador (ADR 0001).
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Politicas.GestionaFlota)]
    [ProducesResponseType<ConductorResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConductorResponse>> Crear(CrearConductorRequest solicitud, CancellationToken ct)
    {
        var tenantId = User.TenantId();
        var userId = User.UserId();
        if (tenantId is null || userId is null)
        {
            return Forbid();
        }

        var conductor = Conductor.Crear(
            tenantId.Value,
            userId.Value,
            solicitud.UserId,
            solicitud.Nombre,
            solicitud.Licencia,
            reloj.GetUtcNow().UtcDateTime);

        db.Conductores.Add(conductor);
        await db.GuardarAsync("Ya hay un conductor con esa licencia en este transportista.", ct);

        return CreatedAtAction(nameof(Obtener), new { id = conductor.ConductorId }, ConductorResponse.De(conductor));
    }

    /// <summary>Lista los conductores de la organización del token. Un servicio los ve todos.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<ConductorResponse>> Listar(CancellationToken ct)
    {
        var consulta = db.Conductores.AsNoTracking();
        if (!User.EsServicio())
        {
            var tenantId = User.TenantId();
            consulta = consulta.Where(c => c.TenantId == tenantId);
        }

        var conductores = await consulta.OrderBy(c => c.Nombre).ToListAsync(ct);
        return conductores.Select(ConductorResponse.De).ToList();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ConductorResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConductorResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var conductor = await db.Conductores.AsNoTracking().FirstOrDefaultAsync(c => c.ConductorId == id, ct)
            ?? throw DomainException.NotFound("No existe un conductor con ese identificador.");

        if (!User.PuedeVer(conductor.TenantId))
        {
            return Forbid();
        }

        return ConductorResponse.De(conductor);
    }
}
