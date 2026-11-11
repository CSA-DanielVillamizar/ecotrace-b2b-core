namespace EcoTrace.FleetManagement.Domain;

/// <summary>
/// Una novedad que el conductor reporta sobre su vehículo mientras hace una carga: una falla, un golpe, un neumático. La acción
/// la encola la app móvil y se sincroniza cuando hay señal, así que puede llegar tarde.
/// </summary>
public sealed class NovedadVehiculo
{
    private NovedadVehiculo()
    {
        Descripcion = string.Empty;
    }

    public Guid NovedadId { get; private set; }

    /// <summary>Vehículo de este mismo contexto: clave foránea real.</summary>
    public Guid VehiculoId { get; private set; }

    [ReferenciaExterna("CargoTracking", "Carga que el vehículo llevaba cuando se reportó la novedad")]
    public Guid CargaId { get; private set; }

    [ReferenciaExterna("Identity", "Persona que reportó la novedad")]
    public Guid ReportadaPorUserId { get; private set; }

    public string Descripcion { get; private set; }

    public DateTime ReportadaEn { get; private set; }

    public static NovedadVehiculo Crear(
        Guid vehiculoId, Guid cargaId, Guid reportadaPorUserId, string? descripcion, DateTime ahoraUtc)
    {
        var limpia = (descripcion ?? string.Empty).Trim();
        if (limpia.Length is < 3 or > 500)
        {
            throw DomainException.Validation("La descripción de la novedad debe tener entre 3 y 500 caracteres.");
        }

        if (cargaId == Guid.Empty)
        {
            throw DomainException.Validation("La novedad necesita la carga que el vehículo llevaba.");
        }

        return new NovedadVehiculo
        {
            NovedadId = Guid.NewGuid(),
            VehiculoId = vehiculoId,
            CargaId = cargaId,
            ReportadaPorUserId = reportadaPorUserId,
            Descripcion = limpia,
            ReportadaEn = ahoraUtc
        };
    }
}
