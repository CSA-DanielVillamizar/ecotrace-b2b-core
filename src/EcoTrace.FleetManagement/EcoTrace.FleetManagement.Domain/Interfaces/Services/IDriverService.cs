using EcoTrace.FleetManagement.Domain.UseCases.Contracts;

namespace EcoTrace.FleetManagement.Domain.Interfaces.Services;

public interface IDriverService
{
    Task<List<DriverResponse>> GetAllAsync(Guid? tenantId = null, CancellationToken cancellationToken = default);
    Task<DriverResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DriverResponse> CreateAsync(CreateDriverRequest request, CancellationToken cancellationToken = default);
    Task<DriverResponse?> UpdateAsync(Guid id, UpdateDriverRequest request, CancellationToken cancellationToken = default);
    Task<DriverResponse?> UpdateStatusAsync(Guid id, UpdateDriverStatusRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}