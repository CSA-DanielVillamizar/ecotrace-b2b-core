using EcoTrace.Identity.Api.Contracts;
using EcoTrace.Identity.Api.Extensions;
using EcoTrace.Identity.Api.Security;
using EcoTrace.Identity.Domain;
using EcoTrace.Identity.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Usuario = EcoTrace.Identity.Domain.User;

namespace EcoTrace.Identity.Api.Controllers;

[ApiController]
[Route("api/users")]
[Produces("application/json")]
[Authorize]
public sealed class UsersController(IdentityDbContext db, TimeProvider reloj, IConfiguration configuracion) : ControllerBase
{
    /// <summary>Crea un usuario dentro de una organización existente.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Administrador)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Crear(CrearUserRequest solicitud, CancellationToken ct)
    {
        var tenantId = solicitud.TenantId!.Value;

        // Un administrador crea usuarios en su organización; solo el operador los crea en cualquiera.
        if (!User.EsOperador() && !User.PerteneceA(tenantId))
        {
            return Forbid();
        }

        if (!await db.Tenants.AnyAsync(t => t.TenantId == tenantId, ct))
        {
            throw DomainException.Validation("La organización indicada no existe.");
        }

        var nombreRol = solicitud.Role!.Trim();
        var rol = await db.Roles.FirstOrDefaultAsync(r => r.Nombre.ToLower() == nombreRol.ToLower(), ct)
            ?? throw DomainException.Validation(
                "El rol indicado no existe. Use Conductor, Supervisor, Administrador o Auditor.");

        var iteraciones = configuracion.GetValue("Seguridad:Pbkdf2Iteraciones", Contrasenas.IteracionesPorDefecto);
        var hash = Contrasenas.Hashear(Contrasenas.Validar(solicitud.Password), iteraciones);
        var usuario = Usuario.Crear(tenantId, rol.RoleId, solicitud.Nombre, solicitud.Email, reloj.GetUtcNow().UtcDateTime, hash);
        db.Users.Add(usuario);
        await db.GuardarAsync("Ya existe un usuario con ese correo electrónico.", ct);

        var respuesta = new UserResponse(
            usuario.UserId, usuario.TenantId, rol.Nombre, usuario.Nombre, usuario.Email, usuario.CreadoEn);
        return CreatedAtAction(nameof(Obtener), new { id = usuario.UserId }, respuesta);
    }

    /// <summary>Lista los usuarios, con filtro opcional por organización.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> Listar([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        // El operador ve todos (con filtro opcional); un administrador solo los de su organización.
        if (!User.EsOperador())
        {
            // Un token sin organización (el de un servicio, por ejemplo) no lista nada: filtrar por "ninguna" mostraría todo.
            if (User.TenantId() is not { } propia || (tenantId is not null && tenantId != propia))
            {
                return Forbid();
            }

            tenantId = propia;
        }

        var consulta = db.Users.AsNoTracking().Include(u => u.Role).AsQueryable();
        if (tenantId is not null)
        {
            consulta = consulta.Where(u => u.TenantId == tenantId);
        }

        var usuarios = await consulta.OrderBy(u => u.Nombre).ToListAsync(ct);
        return usuarios.Select(UserResponse.De).ToList();
    }

    /// <summary>
    /// Cambia el teléfono de la propia cuenta. Es la acción que encola el módulo de Identity de la app: lleva
    /// <c>Idempotency-Key</c>; un teléfono inválido responde 400 de tipo A y una organización suspendida, 409 de tipo B.
    /// </summary>
    [HttpPut("{id:guid}/perfil")]
    [Idempotente]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> ActualizarPerfil(Guid id, ActualizarPerfilRequest solicitud, CancellationToken ct)
    {
        // Cada persona cambia su propio perfil, y nadie más.
        if (User.Sub() != id.ToString())
        {
            return Forbid();
        }

        var usuario = await db.Users.Include(u => u.Role).Include(u => u.Tenant).FirstOrDefaultAsync(u => u.UserId == id, ct)
            ?? throw DomainException.NotFound("No existe un usuario con ese identificador.");

        usuario.ActualizarPerfil(solicitud.Telefono, usuario.Tenant?.Estado == EstadoTenant.Activo);
        await db.GuardarAsync("No se pudo actualizar el perfil.", ct);

        return UserResponse.De(usuario);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var usuario = await db.Users.AsNoTracking().Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == id, ct)
            ?? throw DomainException.NotFound("No existe un usuario con ese identificador.");

        // Cada persona ve su propia cuenta; un administrador ve las de su organización; el operador ve todas.
        var esElMismo = User.Sub() == id.ToString();
        var esAdministradorDeLaOrganizacion = User.Rol() == Roles.Administrador && User.PerteneceA(usuario.TenantId);
        if (!esElMismo && !esAdministradorDeLaOrganizacion && !User.EsOperador())
        {
            return Forbid();
        }

        return UserResponse.De(usuario);
    }
}
