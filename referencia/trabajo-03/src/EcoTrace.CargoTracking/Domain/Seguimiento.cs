namespace EcoTrace.CargoTracking.Domain;

/// <summary>Un evento del recorrido de la carga: dónde estaba y en qué estado quedó.</summary>
public sealed class Seguimiento
{
    private Seguimiento()
    {
        Ubicacion = string.Empty;
    }

    public Guid SeguimientoId { get; private set; }

    public Guid CargaId { get; private set; }

    public EstadoCarga Estado { get; private set; }

    public string Ubicacion { get; private set; }

    public string? Nota { get; private set; }

    public DateTime RegistradoEn { get; private set; }

    internal static Seguimiento Crear(
        Guid cargaId, EstadoCarga estado, string? ubicacion, string? nota, DateTime ahoraUtc)
    {
        var ubicacionLimpia = (ubicacion ?? string.Empty).Trim();
        if (ubicacionLimpia.Length is < 2 or > 120)
        {
            throw DomainException.Validation("La ubicación debe tener entre 2 y 120 caracteres.");
        }

        if (nota is { Length: > 300 })
        {
            throw DomainException.Validation("La nota no puede superar los 300 caracteres.");
        }

        return new Seguimiento
        {
            SeguimientoId = Guid.NewGuid(),
            CargaId = cargaId,
            Estado = estado,
            Ubicacion = ubicacionLimpia,
            Nota = nota,
            RegistradoEn = ahoraUtc
        };
    }
}
