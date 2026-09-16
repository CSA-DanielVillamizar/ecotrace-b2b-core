namespace EcoTrace.Billing.Domain.DTOs;

public class FinancialAuditResponse
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime OccurredAt { get; set; }
}
