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

    /// <summary>Estado actual y autoritativo de la organización (ValidateTenantStatus del ADR 0002).</summary>
    [HttpGet("{id:guid}/estado")]
    [ProducesResponseType<EstadoTenantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EstadoTenantResponse>> ObtenerEstado(Guid id, CancellationToken ct) =>
        EstadoTenantResponse.De(await BuscarAsync(id, tracking: false, ct));

    /// <summary>Suspende la organización. Idempotente: suspender una ya suspendida responde 200 sin cambios.</summary>
    [HttpPost("{id:guid}/suspension")]
    [ProducesResponseType<TenantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenantResponse>> Suspender(Guid id, CancellationToken ct)
    {
        var tenant = await BuscarAsync(id, tracking: true, ct);
        if (tenant.Suspender())
        {
            await db.GuardarAsync("No se pudo suspender la organización.", ct);
        }

        return TenantResponse.De(tenant);
    }

    /// <summary>Reactiva la organización. Idempotente.</summary>
    [HttpPost("{id:guid}/reactivacion")]
    [ProducesResponseType<TenantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TenantResponse>> Reactivar(Guid id, CancellationToken ct)
    {
        var tenant = await BuscarAsync(id, tracking: true, ct);
        if (tenant.Reactivar())
        {
            await db.GuardarAsync("No se pudo reactivar la organización.", ct);
        }

        return TenantResponse.De(tenant);
    }

    private async Task<Tenant> BuscarAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var consulta = tracking ? db.Tenants : db.Tenants.AsNoTracking();
        return await consulta.FirstOrDefaultAsync(t => t.TenantId == id, ct)
            ?? throw DomainException.NotFound("No existe una organización con ese identificador.");
    }
}
