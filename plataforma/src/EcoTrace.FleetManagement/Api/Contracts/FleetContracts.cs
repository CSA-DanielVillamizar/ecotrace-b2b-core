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
    string Nombre, string Licencia, DateTime CreadoEn, EstadoRecurso Estado, Guid? ReservadoParaCargaId)
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

/// <summary>Cuerpo común de reservar y liberar: la carga y los dos recursos que se mueven juntos.</summary>
public sealed record RecursosDeCargaRequest
{
    /// <summary>CargaId emitido por Cargo &amp; Tracking.</summary>
    [Required]
    public Guid? CargaId { get; init; }

    [Required]
    public Guid? VehiculoId { get; init; }

    [Required]
    public Guid? ConductorId { get; init; }
}

/// <summary>
/// Resultado de reservar o liberar. CambioEstado es false cuando la operación ya estaba aplicada
/// (se repitió): el llamador puede reintentar sin miedo a duplicar el efecto.
/// </summary>
public sealed record RecursosDeCargaResponse(
    Guid CargaId, Guid VehiculoId, Guid ConductorId,
    EstadoRecurso EstadoVehiculo, EstadoRecurso EstadoConductor, bool CambioEstado);

public sealed record SimularFalloRequest
{
    /// <summary>Operación que falla: por ahora solo "liberaciones".</summary>
    [Required]
    public string? Operacion { get; init; }

    /// <summary>Cuántas llamadas seguidas fallan con 503. 0 restablece el comportamiento normal.</summary>
    [Required]
    public int? Cantidad { get; init; }
}
