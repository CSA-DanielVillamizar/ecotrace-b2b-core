namespace EcoTrace.Billing.Domain.DTOs;

public class CreateFinancialAuditRequest
{
    public Guid PaymentId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
}
