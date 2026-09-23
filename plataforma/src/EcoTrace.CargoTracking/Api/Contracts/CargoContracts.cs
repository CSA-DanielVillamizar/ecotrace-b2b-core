using System.ComponentModel.DataAnnotations;
using EcoTrace.CargoTracking.Domain;

namespace EcoTrace.CargoTracking.Api.Contracts;

public sealed record CrearCargaRequest
{
    /// <summary>Organización generadora (TenantId emitido por Identity).</summary>
    [Required]
    public Guid? GeneradorTenantId { get; init; }

    /// <summary>Organización transportista (TenantId emitido por Identity).</summary>
    [Required]
    public Guid? TransportistaTenantId { get; init; }

    [Required]
    public string? Descripcion { get; init; }

    [Required]
    public string? Origen { get; init; }

    [Required]
    public string? Destino { get; init; }

    [Required]
    public int? PesoKg { get; init; }
}

public sealed record AsignarCargaRequest
{
    /// <summary>VehiculoId emitido por Fleet Management.</summary>
    [Required]
    public Guid? VehiculoId { get; init; }

    /// <summary>ConductorId emitido por Fleet Management.</summary>
    [Required]
    public Guid? ConductorId { get; init; }
}

public sealed record RegistrarSeguimientoRequest
{
    [Required]
    public EstadoCarga? Estado { get; init; }

    [Required]
    public string? Ubicacion { get; init; }

    public string? Nota { get; init; }
}

public sealed record SeguimientoResponse(
    Guid SeguimientoId, EstadoCarga Estado, string Ubicacion, string? Nota, DateTime RegistradoEn)
{
    public static SeguimientoResponse De(Seguimiento s) =>
        new(s.SeguimientoId, s.Estado, s.Ubicacion, s.Nota, s.RegistradoEn);
}

public sealed record AsignacionResponse(Guid AsignacionId, Guid VehiculoId, Guid ConductorId, DateTime AsignadaEn)
{
    public static AsignacionResponse De(AsignacionCarga a) =>
        new(a.AsignacionId, a.VehiculoId, a.ConductorId, a.AsignadaEn);
}

public sealed record CargaResponse(
    Guid CargaId, Guid GeneradorTenantId, Guid TransportistaTenantId, string Descripcion,
    string Origen, string Destino, int PesoKg, EstadoCarga Estado, DateTime CreadoEn)
{
    public static CargaResponse De(Carga c) =>
        new(c.CargaId, c.GeneradorTenantId, c.TransportistaTenantId, c.Descripcion,
            c.Origen, c.Destino, c.PesoKg, c.Estado, c.CreadoEn);
}

public sealed record CargaDetalleResponse(
    CargaResponse Carga, AsignacionResponse? Asignacion, IReadOnlyList<SeguimientoResponse> Seguimientos)
{
    public static CargaDetalleResponse De(Carga c) =>
        new(CargaResponse.De(c),
            c.Asignacion is null ? null : AsignacionResponse.De(c.Asignacion),
            c.Seguimientos.OrderBy(s => s.RegistradoEn).Select(SeguimientoResponse.De).ToList());
}
