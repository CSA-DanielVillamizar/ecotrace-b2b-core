using EcoTrace.Identity.Domain.Models;

namespace EcoTrace.Identity.Domain.Repositories;

public interface ITenantRepository
{
    Task<List<Tenant>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Tenant?> GetByTaxIdAsync(string taxId, CancellationToken cancellationToken = default);
    Task<bool> ExistsByTaxIdAsync(string taxId, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);
    void Update(Tenant tenant);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}