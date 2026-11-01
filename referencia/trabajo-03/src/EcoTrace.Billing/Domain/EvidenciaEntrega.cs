namespace EcoTrace.Billing.Domain;

/// <summary>
/// Evidencia de que una carga se entregó (una foto, una firma, un acta), que el conductor sube desde la app. Como el resto del
/// historial del pago, es de solo agregado.
/// </summary>
public sealed class EvidenciaEntrega
{
    private EvidenciaEntrega()
    {
        Descripcion = string.Empty;
    }

    public Guid EvidenciaId { get; private set; }

    /// <summary>Pago de este mismo contexto: clave foránea real.</summary>
    public Guid PagoId { get; private set; }

    [ReferenciaExterna("Identity", "Persona que registró la evidencia")]
    public Guid RegistradaPorUserId { get; private set; }

    public string Descripcion { get; private set; }

    /// <summary>Referencia opcional al archivo (el identificador de una foto, por ejemplo). Billing no guarda el archivo.</summary>
    public string? Referencia { get; private set; }

    public DateTime RegistradaEn { get; private set; }

    internal static EvidenciaEntrega Crear(
        Guid pagoId, Guid registradaPorUserId, string? descripcion, string? referencia, DateTime ahoraUtc)
    {
        var limpia = (descripcion ?? string.Empty).Trim();
        if (limpia.Length is < 3 or > 500)
        {
            throw DomainException.Validation("La descripción de la evidencia debe tener entre 3 y 500 caracteres.");
        }

        var ref_ = string.IsNullOrWhiteSpace(referencia) ? null : referencia.Trim();
        if (ref_ is { Length: > 200 })
        {
            throw DomainException.Validation("La referencia no puede pasar de 200 caracteres.");
        }

        return new EvidenciaEntrega
        {
            EvidenciaId = Guid.NewGuid(),
            PagoId = pagoId,
            RegistradaPorUserId = registradaPorUserId,
            Descripcion = limpia,
            Referencia = ref_,
            RegistradaEn = ahoraUtc
        };
    }
}
