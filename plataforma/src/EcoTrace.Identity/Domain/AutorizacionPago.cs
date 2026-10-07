namespace EcoTrace.Identity.Domain;

public enum EstadoAutorizacion
{
    Autorizado = 1,
    Revocado = 2
}

/// <summary>
/// Autorización que Identity emite antes de que Billing libere un pago (ADR 0003, Saga 2, paso 1).
/// Nunca se borra: si el Saga falla más adelante, la compensación la marca Revocado, y una
/// autorización revocada no se reutiliza para otro intento con el mismo PagoId.
/// </summary>
public sealed class AutorizacionPago
{
    private AutorizacionPago()
    {
    }

    public Guid AutorizacionId { get; private set; }

    [ReferenciaExterna("Billing", "Pago cuya liberación autoriza esta autorización")]
    public Guid PagoId { get; private set; }

    /// <summary>Transportista que recibirá los fondos. Es del mismo contexto, por eso es clave foránea real.</summary>
    public Guid TenantId { get; private set; }

    public EstadoAutorizacion Estado { get; private set; }

    public DateTime CreadoEn { get; private set; }

    public DateTime ActualizadoEn { get; private set; }

    public static AutorizacionPago Crear(Guid pagoId, Tenant tenant, DateTime ahoraUtc)
    {
        if (pagoId == Guid.Empty)
        {
            throw DomainException.Validation("La autorización necesita el identificador del pago.");
        }

        if (tenant.TenantType != TenantType.Transportista)
        {
            throw DomainException.Validation("Solo un transportista puede recibir la liberación de un pago.");
        }

        if (tenant.Estado != EstadoTenant.Activo)
        {
            throw DomainException.Conflict("La organización está suspendida y no puede recibir pagos.");
        }

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

    /// <summary>Revoca la autorización. Es idempotente: si ya estaba revocada devuelve false.</summary>
    public bool Revocar(DateTime ahoraUtc)
    {
        if (Estado == EstadoAutorizacion.Revocado)
        {
            return false;
        }

        Estado = EstadoAutorizacion.Revocado;
        ActualizadoEn = ahoraUtc;
        return true;
    }
}
