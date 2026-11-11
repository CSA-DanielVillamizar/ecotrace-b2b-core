using EcoTrace.FleetManagement.Api.Contracts;
using EcoTrace.FleetManagement.Api.Security;
using EcoTrace.FleetManagement.Domain;
using EcoTrace.FleetManagement.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Api.Controllers;

[ApiController]
[Route("api/conductores")]
[Authorize]
[Produces("application/json")]
public sealed class ConductoresController(FleetManagementDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>
    /// Registra un conductor. El TenantId llega en la solicitud y no se valida contra Identity:
    /// entre contextos solo viaja el identificador (ADR 0001).
    /// </summary>
    [HttpPost]
    [Authorize(Roles = Roles.Supervisor + "," + Roles.Administrador)]
    [ProducesResponseType<ConductorResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConductorResponse>> Crear(CrearConductorRequest solicitud, CancellationToken ct)
    {
        if (!User.PerteneceA(solicitud.TenantId!.Value) || User.Sub() != solicitud.RegistradoPorUserId.ToString())
        {
            return Forbid();
        }

        var conductor = Conductor.Crear(
            solicitud.TenantId!.Value,
            solicitud.RegistradoPorUserId!.Value,
            solicitud.UserId,
            solicitud.Nombre,
            solicitud.Licencia,
            reloj.GetUtcNow().UtcDateTime);

        db.Conductores.Add(conductor);
        await db.GuardarAsync("Ya hay un conductor con esa licencia en este transportista.", ct);

        return CreatedAtAction(nameof(Obtener), new { id = conductor.ConductorId }, ConductorResponse.De(conductor));
    }

    /// <summary>Lista los conductores, con filtro opcional por transportista.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConductorResponse>>> Listar([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        if (!User.EsOperador())
        {
            // Un token sin organización (el de un servicio, por ejemplo) no lista nada: filtrar por "ninguna" mostraría todo.
            if (User.TenantId() is not { } propia || (tenantId is not null && tenantId != propia))
            {
                return Forbid();
            }

            tenantId = propia;
        }

        var consulta = db.Conductores.AsNoTracking();
        if (tenantId is not null)
        {
            consulta = consulta.Where(c => c.TenantId == tenantId);
        }

        var conductores = await consulta.OrderBy(c => c.Nombre).ToListAsync(ct);
        return conductores.Select(ConductorResponse.De).ToList();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ConductorResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConductorResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var conductor = await db.Conductores.AsNoTracking().FirstOrDefaultAsync(c => c.ConductorId == id, ct)
            ?? throw DomainException.NotFound("No existe un conductor con ese identificador.");

        if (!User.EsOperador() && !User.PerteneceA(conductor.TenantId))
        {
            return Forbid();
        }

        return ConductorResponse.De(conductor);
    }
}
