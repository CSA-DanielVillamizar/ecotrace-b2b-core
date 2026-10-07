namespace EcoTrace.Billing.Domain;

/// <summary>
/// Pago retenido en Escrow entre dos organizaciones (ADR 0001): el generador deposita, el
/// transportista cobra cuando se libera. CargaId apunta a Cargo &amp; Tracking como valor plano.
/// Billing no comprueba aquí que la carga exista ni que esté entregada: esa coordinación entre
/// contextos es el Saga "Liberar Pago en Escrow" del ADR 0003 (Trabajo 2).
/// </summary>
public sealed class Pago
{
    public const string MonedaPorDefecto = "COP";
    public const decimal MontoMaximo = 10_000_000_000m;

    private readonly List<AuditoriaFinanciera> _auditoria = [];

    private Pago()
    {
        Moneda = MonedaPorDefecto;
    }

    public Guid PagoId { get; private set; }

    [ReferenciaExterna("Identity", "Organización que deposita los fondos")]
    public Guid GeneradorTenantId { get; private set; }

    [ReferenciaExterna("Identity", "Transportista que recibe los fondos al liberarse")]
    public Guid TransportistaTenantId { get; private set; }

    [ReferenciaExterna("CargoTracking", "Carga cuyo servicio respalda el pago")]
    public Guid CargaId { get; private set; }

    public decimal Monto { get; private set; }

    public string Moneda { get; private set; }

    public EstadoEscrow EstadoEscrow { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public DateTime ActualizadoEn { get; private set; }

    public IReadOnlyList<AuditoriaFinanciera> Auditoria => _auditoria;

    public static Pago Crear(
        Guid generadorTenantId, Guid transportistaTenantId, Guid cargaId, decimal monto, DateTime ahoraUtc)
    {
        if (generadorTenantId == Guid.Empty || transportistaTenantId == Guid.Empty)
        {
            throw DomainException.Validation("El pago necesita el generador y el transportista.");
        }

        if (generadorTenantId == transportistaTenantId)
        {
            throw DomainException.Validation("El generador y el transportista deben ser organizaciones distintas.");
        }

        if (cargaId == Guid.Empty)
        {
            throw DomainException.Validation("El pago necesita el identificador de la carga.");
        }

        ValidarMonto(monto);

        var pago = new Pago
        {
            PagoId = Guid.NewGuid(),
            GeneradorTenantId = generadorTenantId,
            TransportistaTenantId = transportistaTenantId,
            CargaId = cargaId,
            Monto = decimal.Round(monto, 2),
            EstadoEscrow = EstadoEscrow.EnCustodia,
            CreadoEn = ahoraUtc,
            ActualizadoEn = ahoraUtc
        };

        pago._auditoria.Add(AuditoriaFinanciera.Crear(
            pago.PagoId, "Pago creado y fondos en custodia", EstadoEscrow.EnCustodia, ahoraUtc));
        return pago;
    }

    /// <summary>Entrega los fondos al transportista. Solo procede desde EnCustodia.</summary>
    public void Liberar(DateTime ahoraUtc) =>
        CambiarEstado(EstadoEscrow.Liberado, "Fondos liberados al transportista", ahoraUtc);

    /// <summary>Devuelve los fondos al generador. Solo procede desde EnCustodia.</summary>
    public void Reembolsar(DateTime ahoraUtc) =>
        CambiarEstado(EstadoEscrow.Reembolsado, "Fondos reembolsados al generador", ahoraUtc);

    /// <summary>
    /// Compensación de Billing en el Saga "Liberar Pago" (ADR 0003): si el Saga falla después de
    /// haber liberado los fondos, se revierten a una retención en disputa. Solo procede desde Liberado.
    /// </summary>
    public void RevertirLiberacion(DateTime ahoraUtc)
    {
        if (EstadoEscrow != EstadoEscrow.Liberado)
        {
            throw DomainException.Conflict(
                $"Solo un pago liberado se puede revertir. Este pago está {EstadoEscrow}.");
        }

        EstadoEscrow = EstadoEscrow.EnDisputa;
        ActualizadoEn = ahoraUtc;
        _auditoria.Add(AuditoriaFinanciera.Crear(
            PagoId, "Liberación revertida: fondos en disputa (nota de débito interna)", EstadoEscrow.EnDisputa, ahoraUtc));
    }

    /// <summary>
    /// Deja constancia de un hecho del proceso (una autorización obtenida, un recurso liberado…) sin
    /// cambiar el estado del dinero. El registro es de solo agregado, como el resto de la auditoría.
    /// </summary>
    public void RegistrarEvento(string accion, DateTime ahoraUtc)
    {
        var limpia = (accion ?? string.Empty).Trim();
        if (limpia.Length is < 3 or > 120)
        {
            throw DomainException.Validation("La acción de auditoría debe tener entre 3 y 120 caracteres.");
        }

        _auditoria.Add(AuditoriaFinanciera.Crear(PagoId, limpia, EstadoEscrow, ahoraUtc));
    }

    internal static void ValidarMonto(decimal monto)
    {
        if (monto <= 0 || monto > MontoMaximo)
        {
            throw DomainException.Validation($"El monto debe ser mayor que cero y no superar {MontoMaximo:N0}.");
        }

        if (decimal.Round(monto, 2) != monto)
        {
            throw DomainException.Validation("El monto admite como máximo dos decimales.");
        }
    }

    private void CambiarEstado(EstadoEscrow nuevo, string accion, DateTime ahoraUtc)
    {
        if (EstadoEscrow != EstadoEscrow.EnCustodia)
        {
            throw DomainException.Conflict(
                $"El pago ya está {EstadoEscrow}. Solo un pago en custodia puede pasar a {nuevo}.");
        }

        EstadoEscrow = nuevo;
        ActualizadoEn = ahoraUtc;
        _auditoria.Add(AuditoriaFinanciera.Crear(PagoId, accion, nuevo, ahoraUtc));
    }
}
