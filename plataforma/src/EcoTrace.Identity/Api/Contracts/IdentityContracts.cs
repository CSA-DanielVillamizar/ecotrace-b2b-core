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

public sealed record TenantResponse(
    Guid TenantId, string Nombre, TenantType TenantType, DateTime CreadoEn, EstadoTenant Estado, int Version)
{
    public static TenantResponse De(Tenant tenant) =>
        new(tenant.TenantId, tenant.Nombre, tenant.TenantType, tenant.CreadoEn, tenant.Estado, tenant.Version);
}

/// <summary>
/// Respuesta de ValidateTenantStatus (ADR 0002). Es la fuente autoritativa del estado: quien va
/// a mover dinero debe preguntar aquí y no fiarse de una copia local.
/// </summary>
public sealed record EstadoTenantResponse(Guid TenantId, EstadoTenant Estado, int Version)
{
    public static EstadoTenantResponse De(Tenant tenant) => new(tenant.TenantId, tenant.Estado, tenant.Version);
}

public sealed record AutorizarPagoRequest
{
    /// <summary>PagoId emitido por Billing &amp; Escrow.</summary>
    [Required]
    public Guid? PagoId { get; init; }

    /// <summary>Transportista que recibirá los fondos (TenantId de esta misma plataforma).</summary>
    [Required]
    public Guid? TenantId { get; init; }
}

public sealed record AutorizacionPagoResponse(
    Guid AutorizacionId, Guid PagoId, Guid TenantId, EstadoAutorizacion Estado, DateTime CreadoEn, DateTime ActualizadoEn)
{
    public static AutorizacionPagoResponse De(AutorizacionPago a) =>
        new(a.AutorizacionId, a.PagoId, a.TenantId, a.Estado, a.CreadoEn, a.ActualizadoEn);
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
