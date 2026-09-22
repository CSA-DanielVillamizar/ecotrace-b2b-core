using EcoTrace.Identity.Api.Contracts;
using EcoTrace.Identity.Domain;
using EcoTrace.Identity.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Usuario = EcoTrace.Identity.Domain.User;

namespace EcoTrace.Identity.Api.Controllers;

[ApiController]
[Route("api/users")]
[Produces("application/json")]
public sealed class UsersController(IdentityDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>Crea un usuario dentro de una organización existente.</summary>
    [HttpPost]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Crear(CrearUserRequest solicitud, CancellationToken ct)
    {
        var tenantId = solicitud.TenantId!.Value;
        if (!await db.Tenants.AnyAsync(t => t.TenantId == tenantId, ct))
        {
            throw DomainException.Validation("La organización indicada no existe.");
        }

        var nombreRol = solicitud.Role!.Trim();
        var rol = await db.Roles.FirstOrDefaultAsync(r => r.Nombre.ToLower() == nombreRol.ToLower(), ct)
            ?? throw DomainException.Validation(
                "El rol indicado no existe. Use Conductor, Supervisor, Administrador o Auditor.");

        var usuario = Usuario.Crear(tenantId, rol.RoleId, solicitud.Nombre, solicitud.Email, reloj.GetUtcNow().UtcDateTime);
        db.Users.Add(usuario);
        await db.GuardarAsync("Ya existe un usuario con ese correo electrónico.", ct);

        var respuesta = new UserResponse(
            usuario.UserId, usuario.TenantId, rol.Nombre, usuario.Nombre, usuario.Email, usuario.CreadoEn);
        return CreatedAtAction(nameof(Obtener), new { id = usuario.UserId }, respuesta);
    }

    /// <summary>Lista los usuarios, con filtro opcional por organización.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<UserResponse>> Listar([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var consulta = db.Users.AsNoTracking().Include(u => u.Role).AsQueryable();
        if (tenantId is not null)
        {
            consulta = consulta.Where(u => u.TenantId == tenantId);
        }

        var usuarios = await consulta.OrderBy(u => u.Nombre).ToListAsync(ct);
        return usuarios.Select(UserResponse.De).ToList();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var usuario = await db.Users.AsNoTracking().Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == id, ct)
            ?? throw DomainException.NotFound("No existe un usuario con ese identificador.");

        return UserResponse.De(usuario);
    }
}
