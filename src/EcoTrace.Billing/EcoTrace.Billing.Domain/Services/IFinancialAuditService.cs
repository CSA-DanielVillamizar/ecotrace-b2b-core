using EcoTrace.Billing.Domain.DTOs;

namespace EcoTrace.Billing.Domain.Services;

public interface IFinancialAuditService
{
    Task<List<FinancialAuditResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<FinancialAuditResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FinancialAuditResponse> CreateAsync(CreateFinancialAuditRequest request, CancellationToken cancellationToken = default);
}
