namespace EcoTrace.CargoTracking.Domain;

/// <summary>Vehículo y conductor que ejecutan una carga (ADR 0001, Issue #5).</summary>
public sealed class AsignacionCarga
{
    private AsignacionCarga()
    {
    }

    public Guid AsignacionId { get; private set; }

    public Guid CargaId { get; private set; }

    [ReferenciaExterna("FleetManagement", "Vehículo que transporta la carga")]
    public Guid VehiculoId { get; private set; }

    [ReferenciaExterna("FleetManagement", "Conductor que transporta la carga")]
    public Guid ConductorId { get; private set; }

    public DateTime AsignadaEn { get; private set; }

    internal static AsignacionCarga Crear(Guid cargaId, Guid vehiculoId, Guid conductorId, DateTime ahoraUtc) =>
        new()
        {
            AsignacionId = Guid.NewGuid(),
            CargaId = cargaId,
            VehiculoId = vehiculoId,
            ConductorId = conductorId,
            AsignadaEn = ahoraUtc
        };
}
