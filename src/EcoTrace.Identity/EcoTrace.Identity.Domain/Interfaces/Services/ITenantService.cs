using EcoTrace.Identity.Domain.UseCases.Contracts;

namespace EcoTrace.Identity.Domain.Interfaces.Services;

public interface ITenantService
{
    Task<List<TenantResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<TenantResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TenantResponse> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken = default);
    Task<TenantResponse?> UpdateAsync(Guid id, UpdateTenantRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}