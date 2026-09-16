using EcoTrace.Cargo_Tracking.Domain.DTOs;

namespace EcoTrace.Cargo_Tracking.Domain.Services;

public interface ICargoService
{
    Task<List<CargoResponse>> GetAllAsync(Guid? generatorTenantId = null, Guid? carrierTenantId = null, CancellationToken cancellationToken = default);
    Task<CargoResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CargoResponse> CreateAsync(CreateCargoRequest request, CancellationToken cancellationToken = default);
    Task<CargoResponse?> UpdateAsync(Guid id, UpdateCargoRequest request, CancellationToken cancellationToken = default);
    Task<CargoResponse?> UpdateStatusAsync(Guid id, UpdateCargoStatusRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}