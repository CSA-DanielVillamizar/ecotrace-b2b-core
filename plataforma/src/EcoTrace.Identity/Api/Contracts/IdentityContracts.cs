using System.ComponentModel.DataAnnotations;
using EcoTrace.Identity.Domain;

namespace EcoTrace.Identity.Api.Contracts;

// Los contratos son el lenguaje publico del servicio. Las entidades de dominio no se exponen:
// asi el esquema interno puede cambiar sin romper a quien consume la API.

public sealed record CrearTenantRequest
{
    [Required]
    public string? Nombre { get; init; }

    [Required]
    public TenantType? TenantType { get; init; }
}

public sealed record TenantResponse(Guid TenantId, string Nombre, TenantType TenantType, EstadoTenant Estado, int Version, DateTime CreadoEn)
{
    public static TenantResponse De(Tenant tenant) =>
        new(tenant.TenantId, tenant.Nombre, tenant.TenantType, tenant.Estado, tenant.Version, tenant.CreadoEn);
}

public sealed record TenantEstadoResponse(Guid TenantId, EstadoTenant Estado, int Version)
{
    public static TenantEstadoResponse De(Tenant tenant) =>
        new(tenant.TenantId, tenant.Estado, tenant.Version);
}

public sealed record AutorizarPagoRequest
{
    [Required]
    public Guid? PagoId { get; init; }

    [Required]
    public Guid? TenantId { get; init; }
}

public sealed record AutorizacionPagoResponse(
    Guid AutorizacionId, Guid PagoId, Guid TenantId, EstadoAutorizacion Estado, DateTime CreadoEn, DateTime ActualizadoEn)
{
    public static AutorizacionPagoResponse De(AutorizacionPago autorizacion) => new(
        autorizacion.AutorizacionId, autorizacion.PagoId, autorizacion.TenantId,
        autorizacion.Estado, autorizacion.CreadoEn, autorizacion.ActualizadoEn);
}

public sealed record CrearUserRequest
{
    [Required]
    public Guid? TenantId { get; init; }

    /// <summary>Nombre del rol: Conductor, Supervisor, Administrador o Auditor.</summary>
    [Required]
    public string? Role { get; init; }

    [Required]
    public string? Nombre { get; init; }

    [Required]
    public string? Email { get; init; }
}

public sealed record UserResponse(
    Guid UserId, Guid TenantId, string Role, string Nombre, string Email, DateTime CreadoEn)
{
    public static UserResponse De(User usuario) =>
        new(usuario.UserId, usuario.TenantId, usuario.Role?.Nombre ?? string.Empty,
            usuario.Nombre, usuario.Email, usuario.CreadoEn);
}

public sealed record RoleResponse(int RoleId, string Nombre, string[] Permisos)
{
    public static RoleResponse De(Role rol) =>
        new(rol.RoleId, rol.Nombre, rol.Claims.Select(c => c.Valor).Order().ToArray());
}
