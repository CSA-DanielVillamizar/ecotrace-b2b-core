using EcoTrace.Identity.Domain.DTOs;

namespace EcoTrace.Identity.Domain.Services;

public interface IUserService
{
    Task<List<UserResponse>> GetAllAsync(Guid? tenantId = null, CancellationToken cancellationToken = default);
    Task<UserResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);
    Task<UserResponse?> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> AssignRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default);
    Task<bool> RemoveRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default);
}