using EcoTrace.Billing.Domain.Enumerations;

namespace EcoTrace.Billing.Domain.DTOs;

public class UpdatePaymentStatusRequest
{
    public EscrowStatus Status { get; set; }
}
