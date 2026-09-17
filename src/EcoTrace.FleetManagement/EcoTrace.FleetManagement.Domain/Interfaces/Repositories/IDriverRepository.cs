using EcoTrace.FleetManagement.Domain.Models;

namespace EcoTrace.FleetManagement.Domain.Interfaces.Repositories;

public interface IDriverRepository
{
    Task<List<Driver>> GetAllAsync(Guid? tenantId = null, CancellationToken cancellationToken = default);
    Task<Driver?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Driver?> GetByLicenseNumberAsync(string licenseNumber, CancellationToken cancellationToken = default);
    Task<bool> ExistsByLicenseNumberAsync(string licenseNumber, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task AddAsync(Driver driver, CancellationToken cancellationToken = default);
    void Update(Driver driver);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}