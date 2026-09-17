using EcoTrace.Identity.Domain.Interfaces.Repositories;
using EcoTrace.Identity.Domain.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Identity.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;

    public UserRepository(UserManager<ApplicationUser> userManager, RoleManager<ApplicationRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public Task<List<ApplicationUser>> GetAllAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var query = _userManager.Users.AsNoTracking();
        if (tenantId.HasValue)
        {
            query = query.Where(u => u.TenantId == tenantId.Value);
        }

        return query.ToListAsync(cancellationToken);
    }

    public Task<ApplicationUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _userManager.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken = default) =>
        await _userManager.GetRolesAsync(user);

    public async Task<UserOperationResult> CreateAsync(ApplicationUser user, string password, CancellationToken cancellationToken = default)
    {
        var result = await _userManager.CreateAsync(user, password);
        return ToOperationResult(result);
    }

    public async Task<UserOperationResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        var result = await _userManager.UpdateAsync(user);
        return ToOperationResult(result);
    }

    public async Task<UserOperationResult> AddToRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken = default)
    {
        var result = await _userManager.AddToRoleAsync(user, roleName);
        return ToOperationResult(result);
    }

    public async Task<UserOperationResult> RemoveFromRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken = default)
    {
        var result = await _userManager.RemoveFromRoleAsync(user, roleName);
        return ToOperationResult(result);
    }

    public Task<bool> RoleExistsAsync(string roleName, CancellationToken cancellationToken = default) =>
        _roleManager.RoleExistsAsync(roleName);

    public async Task<UserOperationResult> CreateRoleAsync(string roleName, CancellationToken cancellationToken = default)
    {
        var result = await _roleManager.CreateAsync(new ApplicationRole(roleName));
        return ToOperationResult(result);
    }

    private static UserOperationResult ToOperationResult(IdentityResult result) =>
        new(result.Succeeded, result.Errors.Select(e => e.Code).ToList());
}
