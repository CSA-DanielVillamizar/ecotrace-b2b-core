using EcoTrace.Billing.Domain.Enumerations;

namespace EcoTrace.Billing.Domain.Models;

public class Payment
{
    public Guid Id { get; set; }

    // Referencia por ID al Bounded Context Cargo & Tracking (ADR 0001: sin FKs entre módulos)
    public Guid CargoId { get; set; }

    public Guid GeneratorTenantId { get; set; }
    public Guid CarrierTenantId { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "COP";
    public EscrowStatus Status { get; set; } = EscrowStatus.InCustody;

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
