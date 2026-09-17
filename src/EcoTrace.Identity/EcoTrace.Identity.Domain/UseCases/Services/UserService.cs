using EcoTrace.Identity.Domain.UseCases.Contracts;
using EcoTrace.Identity.Domain.Constants;
using EcoTrace.Identity.Domain.Models;
using EcoTrace.Identity.Domain.Interfaces.Repositories;
using EcoTrace.Identity.Domain.Interfaces.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EcoTrace.Identity.Domain.UseCases.Services;

public class UserService : IUserService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ITenantRepository _tenantRepository;
    private readonly ILogger<UserService> _logger;

    public UserService(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ITenantRepository tenantRepository,
        ILogger<UserService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _tenantRepository = tenantRepository;
        _logger = logger;
    }

    public async Task<List<UserResponse>> GetAllAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var query = _userManager.Users.AsNoTracking();
        if (tenantId.HasValue)
        {
            query = query.Where(u => u.TenantId == tenantId.Value);
        }

        var users = await query.ToListAsync(cancellationToken);
        var responses = new List<UserResponse>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            responses.Add(new UserResponse(
                user.Id,
                user.TenantId,
                user.FullName,
                user.Email ?? string.Empty,
                user.PhoneNumber,
                user.IsActive,
                roles.ToList(),
                user.CreatedAt
            ));
        }

        return responses;
    }

    public async Task<UserResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        return new UserResponse(
            user.Id,
            user.TenantId,
            user.FullName,
            user.Email ?? string.Empty,
            user.PhoneNumber,
            user.IsActive,
            roles.ToList(),
            user.CreatedAt
        );
    }

    public async Task<UserResponse> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ArgumentException(ErrorMessages.User.FullNameRequired);
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new ArgumentException(ErrorMessages.User.EmailRequired);
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException(ErrorMessages.User.PasswordRequired);
        }

        var tenant = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken);
        if (tenant is null)
        {
            throw new ArgumentException(ErrorMessages.User.TenantRequired);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            FullName = request.FullName.Trim(),
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            PhoneNumber = request.PhoneNumber,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        IdentityResult result;
        try
        {
            result = await _userManager.CreateAsync(user, request.Password);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error interno al crear el usuario {UserId}", user.Id);
            throw;
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning("Identity rechazó la creación del usuario {UserId}: {Errors}", user.Id, string.Join("; ", result.Errors.Select(e => e.Code)));
            throw new ArgumentException("No se pudo crear el usuario.");
        }

        var rolesAssigned = new List<string>();
        if (request.Roles != null && request.Roles.Count > 0)
        {
            foreach (var role in request.Roles)
            {
                if (!await _roleManager.RoleExistsAsync(role))
                {
                    await _roleManager.CreateAsync(new ApplicationRole(role));
                }
                await _userManager.AddToRoleAsync(user, role);
                rolesAssigned.Add(role);
            }
        }

        return new UserResponse(
            user.Id,
            user.TenantId,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            user.IsActive,
            rolesAssigned,
            user.CreatedAt
        );
    }

    public async Task<UserResponse?> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ArgumentException(ErrorMessages.User.FullNameRequired);
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = request.PhoneNumber;
        user.IsActive = request.IsActive;
        user.UpdatedAt = DateTime.UtcNow;

        IdentityResult result;
        try
        {
            result = await _userManager.UpdateAsync(user);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error interno al actualizar el usuario {UserId}", id);
            throw;
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning("Identity rechazó la actualización del usuario {UserId}: {Errors}", id, string.Join("; ", result.Errors.Select(e => e.Code)));
            throw new ArgumentException("No se pudo actualizar el usuario.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        return new UserResponse(
            user.Id,
            user.TenantId,
            user.FullName,
            user.Email ?? string.Empty,
            user.PhoneNumber,
            user.IsActive,
            roles.ToList(),
            user.CreatedAt
        );
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return false;
        }

        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.IsActive = false;

        var result = await _userManager.UpdateAsync(user);
        return result.Succeeded;
    }

    public async Task<bool> AssignRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw new ArgumentException(ErrorMessages.User.UserNotFound);
        }

        if (!await _roleManager.RoleExistsAsync(roleName))
        {
            await _roleManager.CreateAsync(new ApplicationRole(roleName));
        }

        IdentityResult result;
        try
        {
            result = await _userManager.AddToRoleAsync(user, roleName);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error interno al asignar el rol {RoleName} al usuario {UserId}", roleName, userId);
            throw;
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning("Identity rechazó la asignación del rol {RoleName} al usuario {UserId}: {Errors}", roleName, userId, string.Join("; ", result.Errors.Select(e => e.Code)));
        }

        return result.Succeeded;
    }

    public async Task<bool> RemoveRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            throw new ArgumentException(ErrorMessages.User.UserNotFound);
        }

        IdentityResult result;
        try
        {
            result = await _userManager.RemoveFromRoleAsync(user, roleName);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error interno al remover el rol {RoleName} del usuario {UserId}", roleName, userId);
            throw;
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning("Identity rechazó la remoción del rol {RoleName} del usuario {UserId}: {Errors}", roleName, userId, string.Join("; ", result.Errors.Select(e => e.Code)));
        }

        return result.Succeeded;
    }
}