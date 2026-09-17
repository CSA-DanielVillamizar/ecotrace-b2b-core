using EcoTrace.Billing.Domain.Models;

namespace EcoTrace.Billing.Domain.Interfaces.Repositories;

public interface IFinancialAuditRepository
{
    Task<List<FinancialAudit>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<FinancialAudit?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(FinancialAudit audit, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
