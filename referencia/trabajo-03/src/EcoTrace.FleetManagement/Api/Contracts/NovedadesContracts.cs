using System.ComponentModel.DataAnnotations;
using EcoTrace.FleetManagement.Domain;

namespace EcoTrace.FleetManagement.Api.Contracts;

public sealed record ReportarNovedadRequest
{
    /// <summary>Carga que el vehículo llevaba cuando ocurrió la novedad.</summary>
    [Required]
    public Guid? CargaId { get; init; }

    [Required]
    public string? Descripcion { get; init; }
}

public sealed record NovedadResponse(
    Guid NovedadId, Guid VehiculoId, Guid CargaId, Guid ReportadaPorUserId, string Descripcion, DateTime ReportadaEn)
{
    public static NovedadResponse De(NovedadVehiculo n) =>
        new(n.NovedadId, n.VehiculoId, n.CargaId, n.ReportadaPorUserId, n.Descripcion, n.ReportadaEn);
}
