namespace EcoTrace.Billing.Domain.DTOs;

public class UpdateInvoiceRequest
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "COP";
}
