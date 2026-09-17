using EcoTrace.Cargo_Tracking.Domain.Models;

namespace EcoTrace.Cargo_Tracking.Domain.Interfaces.Repositories;

public interface ICargoRepository
{
    Task<List<Cargo>> GetAllAsync(Guid? generatorTenantId = null, Guid? carrierTenantId = null, CancellationToken cancellationToken = default);
    Task<Cargo?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Cargo cargo, CancellationToken cancellationToken = default);
    void Update(Cargo cargo);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}