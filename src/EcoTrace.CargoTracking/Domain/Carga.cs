namespace EcoTrace.CargoTracking.Domain;

// Representa la carga/envío en sí. Nombre y campos tomados
// directamente del ADR 0001 (sección "Cargo & Tracking").
//
// Nota: aquí YA NO va Estado ni VehiculoId — esos se movieron a
// Seguimiento y AsignacionCarga respectivamente, tal como el ADR
// separa estas 3 entidades.
public class Carga
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // --- Referencias externas (Identity) ---
    // El ADR describe a Carga como una entidad de "dos tenants":
    // el generador (dueño de la carga) y el transportista (quien
    // la mueve). Ambos son IDs planos, nunca el Tenant completo.
    public Guid GeneradorTenantId { get; set; }
    public Guid TransportistaTenantId { get; set; }

    public string Origen { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    // --- Relaciones INTERNAS (mismo Bounded Context) ---
    public ICollection<AsignacionCarga> Asignaciones { get; set; } = new List<AsignacionCarga>();
    public ICollection<Seguimiento> Seguimientos { get; set; } = new List<Seguimiento>();
}