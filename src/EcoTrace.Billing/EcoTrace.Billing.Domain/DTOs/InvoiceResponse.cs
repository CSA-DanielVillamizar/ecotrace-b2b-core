namespace EcoTrace.Billing.Domain.DTOs;

public class InvoiceResponse
{
    public Guid Id { get; set; }
    public Guid CargoId { get; set; }
    public Guid GeneratorTenantId { get; set; }
    public Guid CarrierTenantId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "COP";
    public DateTime IssuedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
