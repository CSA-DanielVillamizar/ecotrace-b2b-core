namespace EcoTrace.Billing.Domain.Enumerations;

/// <summary>Estado del escrow de un pago, ver ADR 0001.</summary>
public enum EscrowStatus
{
    InCustody,
    Released,
    Refunded
}
