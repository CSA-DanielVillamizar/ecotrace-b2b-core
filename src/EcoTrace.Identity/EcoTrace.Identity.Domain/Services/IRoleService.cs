using EcoTrace.Identity.Domain.DTOs;

namespace EcoTrace.Identity.Domain.Services;

public interface IRoleService
{
    Task<List<RoleResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RoleResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RoleResponse> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}