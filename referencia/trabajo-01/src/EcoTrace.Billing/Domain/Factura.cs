namespace EcoTrace.Billing.Domain;

/// <summary>
/// Comprobante del servicio prestado (ADR 0001, "Facturación"). Una carga tiene como máximo
/// una factura. Los identificadores de Identity y Cargo &amp; Tracking son valores planos.
/// </summary>
public sealed class Factura
{
    private Factura()
    {
        Numero = string.Empty;
    }

    public Guid FacturaId { get; private set; }

    public string Numero { get; private set; }

    [ReferenciaExterna("CargoTracking", "Carga facturada")]
    public Guid CargaId { get; private set; }

    [ReferenciaExterna("Identity", "Organización a la que se factura")]
    public Guid GeneradorTenantId { get; private set; }

    [ReferenciaExterna("Identity", "Transportista que emite el cobro")]
    public Guid TransportistaTenantId { get; private set; }

    public decimal Monto { get; private set; }

    public DateTime EmitidaEn { get; private set; }

    public static Factura Emitir(
        Guid generadorTenantId, Guid transportistaTenantId, Guid cargaId, decimal monto, DateTime ahoraUtc)
    {
        if (generadorTenantId == Guid.Empty || transportistaTenantId == Guid.Empty || cargaId == Guid.Empty)
        {
            throw DomainException.Validation("La factura necesita generador, transportista y carga.");
        }

        if (generadorTenantId == transportistaTenantId)
        {
            throw DomainException.Validation("El generador y el transportista deben ser organizaciones distintas.");
        }

        Pago.ValidarMonto(monto);

        return new Factura
        {
            FacturaId = Guid.NewGuid(),
            Numero = $"FAC-{ahoraUtc:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            CargaId = cargaId,
            GeneradorTenantId = generadorTenantId,
            TransportistaTenantId = transportistaTenantId,
            Monto = decimal.Round(monto, 2),
            EmitidaEn = ahoraUtc
        };
    }
}
