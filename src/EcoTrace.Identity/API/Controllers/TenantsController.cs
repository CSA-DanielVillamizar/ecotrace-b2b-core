using EcoTrace.Identity.Api.Contracts;
using EcoTrace.Identity.Domain;
using EcoTrace.Identity.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Identity.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TenantsController : ControllerBase
{
    private readonly IdentityDbContext _dbContext;

    public TenantsController(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TenantResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var tenants = await _dbContext.Tenants
            .Select(t => new TenantResponse(t.Id, t.Name, t.TenantType))
            .ToListAsync(cancellationToken);

        return Ok(tenants);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TenantResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
        {
            return NotFound();
        }

        return Ok(new TenantResponse(tenant.Id, tenant.Name, tenant.TenantType));
    }

    [HttpPost]
    public async Task<ActionResult<TenantResponse>> Create(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = request.Name, TenantType = request.TenantType };

        try
        {
            _dbContext.Tenants.Add(tenant);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "No se pudo crear el tenant.",
                Detail = "El registro viola una restricción de la base de datos (por ejemplo, un valor duplicado o un campo requerido faltante).",
                Status = StatusCodes.Status409Conflict,
                Extensions = { ["error"] = ex.InnerException?.Message ?? ex.Message }
            });
        }

        var response = new TenantResponse(tenant.Id, tenant.Name, tenant.TenantType);
        return CreatedAtAction(nameof(GetById), new { id = tenant.Id }, response);
    }
}

