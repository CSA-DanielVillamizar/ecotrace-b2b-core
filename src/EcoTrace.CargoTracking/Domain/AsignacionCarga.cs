namespace EcoTrace.CargoTracking.Domain;

// Nueva entidad exigida por el ADR 0001: conecta un vehículo y un
// conductor (ambos de Fleet Management) con una Carga específica.
// El ADR la describe literalmente como "vehículo + conductor + cargo".
public class AsignacionCarga
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // --- Relación INTERNA con Carga (mismo Bounded Context) ---
    public Guid CargaId { get; set; }
    public Carga? Carga { get; set; }

    // --- Referencias externas (Fleet Management) ---
    // Solo IDs planos: nunca traemos el Vehículo ni el Conductor
    // completos, tal como exige el ADR 0001.
    public Guid VehiculoId { get; set; }
    public Guid ConductorId { get; set; }

    public DateTime FechaAsignacion { get; set; } = DateTime.UtcNow;
}