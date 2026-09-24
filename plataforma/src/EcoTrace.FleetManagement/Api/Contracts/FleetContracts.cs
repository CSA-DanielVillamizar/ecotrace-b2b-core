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
    string Nombre, string Licencia, DateTime CreadoEn,
    EstadoRecurso Estado, Guid? ReservadoParaCargaId)
{
    public static ConductorResponse De(Conductor c) =>
        new(c.ConductorId, c.TenantId, c.RegistradoPorUserId, c.UserId, c.Nombre, c.Licencia, c.CreadoEn,
            c.Estado, c.ReservadoParaCargaId);
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
    Guid VehiculoId, Guid TenantId, Guid RegistradoPorUserId, string Placa, int CapacidadKg, DateTime CreadoEn,
    EstadoRecurso Estado, Guid? ReservadoParaCargaId)
{
    public static VehiculoResponse De(Vehiculo v) =>
        new(v.VehiculoId, v.TenantId, v.RegistradoPorUserId, v.Placa, v.CapacidadKg, v.CreadoEn,
            v.Estado, v.ReservadoParaCargaId);
}

/// <summary>Cuerpo común de POST /api/reservas y POST /api/liberaciones (Trabajo 2).</summary>
public sealed record AsignacionRecursosRequest
{
    [Required]
    public Guid? CargaId { get; init; }

    [Required]
    public Guid? VehiculoId { get; init; }

    [Required]
    public Guid? ConductorId { get; init; }
}

/// <summary>
/// Resultado de reservar o liberar. <c>CambioEstado</c> es false cuando la operación ya estaba
/// aplicada, que es como el llamador distingue una repetición de un cambio real.
/// </summary>
public sealed record AsignacionRecursosResponse(
    Guid CargaId, Guid VehiculoId, Guid ConductorId,
    EstadoRecurso EstadoVehiculo, EstadoRecurso EstadoConductor, bool CambioEstado)
{
    public static AsignacionRecursosResponse De(Guid cargaId, Vehiculo vehiculo, Conductor conductor, bool cambioEstado) =>
        new(cargaId, vehiculo.VehiculoId, conductor.ConductorId,
            vehiculo.Estado, conductor.Estado, cambioEstado);
}

/// <summary>Arma los próximos fallos simulados de una operación. Con cantidad 0 se restablece.</summary>
public sealed record ArmarFalloRequest
{
    [Required]
    public string? Operacion { get; init; }

    [Required]
    [Range(0, 1000)]
    public int? Cantidad { get; init; }
}

/// <summary>Cuántos fallos simulados le quedan a cada operación.</summary>
public sealed record FallosSimuladosResponse(IReadOnlyDictionary<string, int> Restantes);
