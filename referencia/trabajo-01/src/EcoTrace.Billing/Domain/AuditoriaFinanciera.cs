namespace EcoTrace.Billing.Domain;

/// <summary>
/// Registro de solo agregado: cada cambio del Escrow deja una fila y ninguna se edita ni se borra.
/// Solo Pago puede crearla, por eso el constructor es interno.
/// </summary>
public sealed class AuditoriaFinanciera
{
    private AuditoriaFinanciera()
    {
        Accion = string.Empty;
    }

    public Guid AuditoriaId { get; private set; }

    public Guid PagoId { get; private set; }

    public string Accion { get; private set; }

    public EstadoEscrow EstadoResultante { get; private set; }

    public DateTime OcurridoEn { get; private set; }

    internal static AuditoriaFinanciera Crear(Guid pagoId, string accion, EstadoEscrow estado, DateTime ahoraUtc) =>
        new()
        {
            AuditoriaId = Guid.NewGuid(),
            PagoId = pagoId,
            Accion = accion,
            EstadoResultante = estado,
            OcurridoEn = ahoraUtc
        };
}
