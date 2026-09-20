using EcoTrace.Identity.Api.Contracts;
using EcoTrace.Identity.Domain;
using EcoTrace.Identity.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Identity.Api.Controllers;

[ApiController]
[Route("api/tenants")]
[Produces("application/json")]
public sealed class TenantsController(IdentityDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>Registra una organización (Generador o Transportista).</summary>
    [HttpPost]
    [ProducesResponseType<TenantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TenantResponse>> Crear(CrearTenantRequest solicitud, CancellationToken ct)
    {
        var tenant = Tenant.Crear(solicitud.Nombre, solicitud.TenantType!.Value, reloj.GetUtcNow().UtcDateTime);
        db.Tenants.Add(tenant);
        await db.GuardarAsync("Ya existe una organización con ese identificador.", ct);

        return CreatedAtAction(nameof(Obtener), new { id = tenant.TenantId }, TenantResponse.De(tenant));
    }

    /// <summary>Lista las organizaciones, con filtro opcional por tipo.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<TenantResponse>> Listar([FromQuery] TenantType? tipo, CancellationToken ct)
    {
        var consulta = db.Tenants.AsNoTracking();
        if (tipo is not null)
        {
            consulta = consulta.Where(t => t.TenantType == tipo);
        }

        var tenants = await consulta.OrderBy(t => t.Nombre).ToListAsync(ct);
        return tenants.Select(TenantResponse.De).ToList();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<TenantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenantResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == id, ct)
            ?? throw DomainException.NotFound("No existe una organización con ese identificador.");

        return TenantResponse.De(tenant);
    }
}
