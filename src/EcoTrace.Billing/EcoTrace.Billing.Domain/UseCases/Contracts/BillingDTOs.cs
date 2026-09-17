using EcoTrace.Billing.Domain.Enumerations;

namespace EcoTrace.Billing.Domain.UseCases.Contracts;

public record CreateInvoiceRequest(
    Guid CargoId,
    Guid GeneratorTenantId,
    Guid CarrierTenantId,
    decimal Amount,
    string Currency = "COP"
);

public record UpdateInvoiceRequest(
    decimal Amount,
    string Currency = "COP"
);

public record InvoiceResponse(
    Guid Id,
    Guid CargoId,
    Guid GeneratorTenantId,
    Guid CarrierTenantId,
    decimal Amount,
    string Currency,
    InvoiceStatus Status,
    DateTime IssuedAt,
    DateTime CreatedAt
);

public record CreatePaymentRequest(
    Guid CargoId,
    Guid GeneratorTenantId,
    Guid CarrierTenantId,
    decimal Amount,
    string Currency = "COP"
);

public record UpdatePaymentStatusRequest(
    EscrowStatus Status
);

public record PaymentResponse(
    Guid Id,
    Guid CargoId,
    Guid GeneratorTenantId,
    Guid CarrierTenantId,
    decimal Amount,
    string Currency,
    EscrowStatus Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record CreateFinancialAuditRequest(
    Guid PaymentId,
    string Action,
    string? Details
);

public record FinancialAuditResponse(
    Guid Id,
    Guid PaymentId,
    string Action,
    string? Details,
    DateTime OccurredAt
);
