using EcoTrace.FleetManagement.Domain.Models;

namespace EcoTrace.FleetManagement.Domain.Repositories;

public interface IVehicleRepository
{
    Task<List<Vehicle>> GetAllAsync(Guid? tenantId = null, CancellationToken cancellationToken = default);
    Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Vehicle?> GetByPlateNumberAsync(string plateNumber, CancellationToken cancellationToken = default);
    Task<bool> ExistsByPlateNumberAsync(string plateNumber, Guid? excludeId = null, CancellationToken cancellationToken = default);
    Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default);
    void Update(Vehicle vehicle);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}