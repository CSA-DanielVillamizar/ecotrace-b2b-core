using EcoTrace.Cargo_Tracking.Domain.Models;

namespace EcoTrace.Cargo_Tracking.Domain.Interfaces.Repositories;

public interface ICargoAssignmentRepository
{
    Task<List<CargoAssignment>> GetAllAsync(Guid? cargoId = null, Guid? driverId = null, Guid? vehicleId = null, CancellationToken cancellationToken = default);
    Task<CargoAssignment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CargoAssignment?> GetActiveByCargoIdAsync(Guid cargoId, CancellationToken cancellationToken = default);
    Task AddAsync(CargoAssignment assignment, CancellationToken cancellationToken = default);
    void Update(CargoAssignment assignment);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}