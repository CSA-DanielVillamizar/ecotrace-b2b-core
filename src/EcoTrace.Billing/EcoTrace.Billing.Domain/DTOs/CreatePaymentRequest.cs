namespace EcoTrace.Billing.Domain.DTOs;

public class CreatePaymentRequest
{
    public Guid CargoId { get; set; }
    public Guid GeneratorTenantId { get; set; }
    public Guid CarrierTenantId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "COP";
}
