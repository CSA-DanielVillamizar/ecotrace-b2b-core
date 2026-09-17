using EcoTrace.Identity.Domain.Models;

namespace EcoTrace.Identity.Domain.Interfaces.Repositories;

public record UserOperationResult(bool Succeeded, IReadOnlyList<string> Errors);

public interface IUserRepository
{
    Task<List<ApplicationUser>> GetAllAsync(Guid? tenantId = null, CancellationToken cancellationToken = default);
    Task<ApplicationUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken = default);
    Task<UserOperationResult> CreateAsync(ApplicationUser user, string password, CancellationToken cancellationToken = default);
    Task<UserOperationResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default);
    Task<UserOperationResult> AddToRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken = default);
    Task<UserOperationResult> RemoveFromRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken = default);
    Task<bool> RoleExistsAsync(string roleName, CancellationToken cancellationToken = default);
    Task<UserOperationResult> CreateRoleAsync(string roleName, CancellationToken cancellationToken = default);
}
