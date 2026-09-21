using EcoTrace.Identity.Api.Contracts;
using EcoTrace.Identity.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Identity.Api.Controllers;

[ApiController]
[Route("api/roles")]
[Produces("application/json")]
public sealed class RolesController(IdentityDbContext db) : ControllerBase
{
    /// <summary>Lista los roles y los permisos que cada uno otorga.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<RoleResponse>> Listar(CancellationToken ct)
    {
        var roles = await db.Roles.AsNoTracking().Include(r => r.Claims).OrderBy(r => r.RoleId).ToListAsync(ct);
        return roles.Select(RoleResponse.De).ToList();
    }
}
