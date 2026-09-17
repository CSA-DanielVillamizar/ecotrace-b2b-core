using EcoTrace.Billing.Domain.Enumerations;

namespace EcoTrace.Billing.Domain.Models;

public class Invoice
{
    public Guid Id { get; set; }

    // Referencia por ID al Bounded Context Cargo & Tracking (ADR 0001: sin FKs entre módulos)
    public Guid CargoId { get; set; }

    // Marketplace de dos lados: la factura involucra dos tenants (ADR 0001)
    public Guid GeneratorTenantId { get; set; }
    public Guid CarrierTenantId { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "COP";
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Created;
    public DateTime IssuedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
