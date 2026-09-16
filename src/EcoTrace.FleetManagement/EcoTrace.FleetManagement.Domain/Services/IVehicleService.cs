using EcoTrace.FleetManagement.Domain.DTOs;

namespace EcoTrace.FleetManagement.Domain.Services;

public interface IVehicleService
{
    Task<List<VehicleResponse>> GetAllAsync(Guid? tenantId = null, CancellationToken cancellationToken = default);
    Task<VehicleResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<VehicleResponse> CreateAsync(CreateVehicleRequest request, CancellationToken cancellationToken = default);
    Task<VehicleResponse?> UpdateAsync(Guid id, UpdateVehicleRequest request, CancellationToken cancellationToken = default);
    Task<VehicleResponse?> UpdateStatusAsync(Guid id, UpdateVehicleStatusRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}