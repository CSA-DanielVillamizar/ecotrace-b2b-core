using EcoTrace.Billing.Domain.UseCases.Contracts;

namespace EcoTrace.Billing.Domain.Interfaces.Services;

public interface IFinancialAuditService
{
    Task<List<FinancialAuditResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<FinancialAuditResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FinancialAuditResponse> CreateAsync(CreateFinancialAuditRequest request, CancellationToken cancellationToken = default);
}
