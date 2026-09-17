namespace EcoTrace.CargoTracking.Domain;

// Los 4 estados exactos que define el ADR 0001 para el recorrido
// de una Carga. IMPORTANTE: los valores nuevos siempre se agregan
// al final, nunca en medio (ver explicación en versiones anteriores
// de este enum).
public enum EstadoRecorrido
{
    Asignado,     // 0
    EnTransito,   // 1
    ConNovedad,   // 2
    Entregado     // 3
}

// Representa un punto en el tiempo del recorrido de una Carga.
// El ADR lo describe como "ubicación y estado del recorrido".
// Antes llamábamos a esto TrackingEvent y guardábamos el Estado
// en Carga — el ADR real lo ubica aquí, en Seguimiento.
public class Seguimiento
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // --- Relación INTERNA con Carga (mismo Bounded Context) ---
    public Guid CargaId { get; set; }
    public Carga? Carga { get; set; }

    public string Ubicacion { get; set; } = string.Empty;
    public EstadoRecorrido Estado { get; set; } = EstadoRecorrido.Asignado;
    public string Descripcion { get; set; } = string.Empty;
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}