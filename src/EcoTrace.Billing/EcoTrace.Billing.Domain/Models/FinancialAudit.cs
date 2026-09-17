namespace EcoTrace.Billing.Domain.Models;

/// <summary>Registro append-only de trazabilidad financiera (ADR 0001): nunca se actualiza ni se borra.</summary>
public class FinancialAudit
{
    public Guid Id { get; set; }
    public Guid PaymentId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime OccurredAt { get; set; }
}
