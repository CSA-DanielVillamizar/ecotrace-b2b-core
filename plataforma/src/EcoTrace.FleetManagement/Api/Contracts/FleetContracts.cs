using System.ComponentModel.DataAnnotations;
using EcoTrace.FleetManagement.Domain;

namespace EcoTrace.FleetManagement.Api.Contracts;

public sealed record CrearConductorRequest
{
    /// <summary>Transportista dueño de la flota (TenantId emitido por Identity).</summary>
    [Required]
    public Guid? TenantId { get; init; }

    /// <summary>Usuario de Identity que realiza el registro.</summary>
    [Required]
    public Guid? RegistradoPorUserId { get; init; }

    /// <summary>Cuenta de usuario del propio conductor, si ya existe.</summary>
    public Guid? UserId { get; init; }

    [Required]
    public string? Nombre { get; init; }

    [Required]
    public string? Licencia { get; init; }
}

public sealed record ConductorResponse(
    Guid ConductorId, Guid TenantId, Guid RegistradoPorUserId, Guid? UserId,
    string Nombre, string Licencia, DateTime CreadoEn)
{
    public static ConductorResponse De(Conductor c) =>
        new(c.ConductorId, c.TenantId, c.RegistradoPorUserId, c.UserId, c.Nombre, c.Licencia, c.CreadoEn);
}

public sealed record CrearVehiculoRequest
{
    [Required]
    public Guid? TenantId { get; init; }

    [Required]
    public Guid? RegistradoPorUserId { get; init; }

    [Required]
    public string? Placa { get; init; }

    [Required]
    public int? CapacidadKg { get; init; }
}

public sealed record VehiculoResponse(
    Guid VehiculoId, Guid TenantId, Guid RegistradoPorUserId, string Placa, int CapacidadKg, DateTime CreadoEn)
{
    public static VehiculoResponse De(Vehiculo v) =>
        new(v.VehiculoId, v.TenantId, v.RegistradoPorUserId, v.Placa, v.CapacidadKg, v.CreadoEn);
}
