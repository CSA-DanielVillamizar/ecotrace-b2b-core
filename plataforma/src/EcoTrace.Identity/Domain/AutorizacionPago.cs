namespace EcoTrace.Identity.Domain;

/// <summary>
/// Autorizacion del transportista para liberar un pago (paso 1 del Saga Liberar Pago en Escrow,
/// ADR 0003 / Trabajo 2). Hay una sola autorizacion por PagoId (clave unica en
/// <see cref="Infrastructure.Configurations.AutorizacionPagoConfiguration"/>): nunca se borra,
/// y una revocada no se reutiliza.
/// </summary>
public sealed class AutorizacionPago
{
    private AutorizacionPago()
    {
    }

    public Guid AutorizacionId { get; private set; }

    [ReferenciaExterna("Billing", "Pago cuya liberación se autoriza")]
    public Guid PagoId { get; private set; }

    public Guid TenantId { get; private set; }

    public Tenant? Tenant { get; private set; }

    public EstadoAutorizacion Estado { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public DateTime ActualizadoEn { get; private set; }

    public static AutorizacionPago Autorizar(Guid pagoId, Tenant tenant, DateTime ahoraUtc)
    {
        if (pagoId == Guid.Empty)
        {
            throw DomainException.Validation("El PagoId es obligatorio.");
        }

        tenant.AsegurarPuedeAutorizarPago();

        return new AutorizacionPago
        {
            AutorizacionId = Guid.NewGuid(),
            PagoId = pagoId,
            TenantId = tenant.TenantId,
            Estado = EstadoAutorizacion.Autorizado,
            CreadoEn = ahoraUtc,
            ActualizadoEn = ahoraUtc
        };
    }

    /// <summary>Idempotente: si ya estaba Revocado, no cambia nada.</summary>
    public void Revocar(DateTime ahoraUtc)
    {
        if (Estado == EstadoAutorizacion.Revocado)
        {
            return;
        }

        Estado = EstadoAutorizacion.Revocado;
        ActualizadoEn = ahoraUtc;
    }
}
