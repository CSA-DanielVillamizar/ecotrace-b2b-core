using EcoTrace.Cargo_Tracking.Domain.DTOs;

namespace EcoTrace.Cargo_Tracking.Domain.Services;

public interface ICargoAssignmentService
{
    Task<List<CargoAssignmentResponse>> GetAllAsync(Guid? cargoId = null, Guid? driverId = null, Guid? vehicleId = null, CancellationToken cancellationToken = default);
    Task<CargoAssignmentResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CargoAssignmentResponse> CreateAsync(CreateCargoAssignmentRequest request, CancellationToken cancellationToken = default);
    Task<CargoAssignmentResponse?> UpdateStatusAsync(Guid id, UpdateAssignmentStatusRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}