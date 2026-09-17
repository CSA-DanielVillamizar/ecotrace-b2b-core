using EcoTrace.Identity.Domain.UseCases.Contracts;
using EcoTrace.Identity.Domain.Constants;
using EcoTrace.Identity.Domain.Models;
using EcoTrace.Identity.Domain.Interfaces.Repositories;
using EcoTrace.Identity.Domain.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace EcoTrace.Identity.Domain.UseCases.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        ITenantRepository tenantRepository,
        ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _tenantRepository = tenantRepository;
        _logger = logger;
    }

    public async Task<List<UserResponse>> GetAllAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var users = await _userRepository.GetAllAsync(tenantId, cancellationToken);
        var responses = new List<UserResponse>();

        foreach (var user in users)
        {
            var roles = await _userRepository.GetRolesAsync(user, cancellationToken);
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
        var user = await _userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var roles = await _userRepository.GetRolesAsync(user, cancellationToken);
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

        UserOperationResult result;
        try
        {
            result = await _userRepository.CreateAsync(user, request.Password, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error interno al crear el usuario {UserId}", user.Id);
            throw;
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning("Identity rechazó la creación del usuario {UserId}: {Errors}", user.Id, string.Join("; ", result.Errors));
            throw new ArgumentException("No se pudo crear el usuario.");
        }

        var rolesAssigned = new List<string>();
        if (request.Roles != null && request.Roles.Count > 0)
        {
            foreach (var role in request.Roles)
            {
                if (!await _userRepository.RoleExistsAsync(role, cancellationToken))
                {
                    await _userRepository.CreateRoleAsync(role, cancellationToken);
                }
                await _userRepository.AddToRoleAsync(user, role, cancellationToken);
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
        var user = await _userRepository.GetByIdAsync(id, cancellationToken);
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

        UserOperationResult result;
        try
        {
            result = await _userRepository.UpdateAsync(user, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error interno al actualizar el usuario {UserId}", id);
            throw;
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning("Identity rechazó la actualización del usuario {UserId}: {Errors}", id, string.Join("; ", result.Errors));
            throw new ArgumentException("No se pudo actualizar el usuario.");
        }

        var roles = await _userRepository.GetRolesAsync(user, cancellationToken);
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
        var user = await _userRepository.GetByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return false;
        }

        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.IsActive = false;

        var result = await _userRepository.UpdateAsync(user, cancellationToken);
        return result.Succeeded;
    }

    public async Task<bool> AssignRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new ArgumentException(ErrorMessages.User.UserNotFound);
        }

        if (!await _userRepository.RoleExistsAsync(roleName, cancellationToken))
        {
            await _userRepository.CreateRoleAsync(roleName, cancellationToken);
        }

        UserOperationResult result;
        try
        {
            result = await _userRepository.AddToRoleAsync(user, roleName, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error interno al asignar el rol {RoleName} al usuario {UserId}", roleName, userId);
            throw;
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning("Identity rechazó la asignación del rol {RoleName} al usuario {UserId}: {Errors}", roleName, userId, string.Join("; ", result.Errors));
        }

        return result.Succeeded;
    }

    public async Task<bool> RemoveRoleAsync(Guid userId, string roleName, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new ArgumentException(ErrorMessages.User.UserNotFound);
        }

        UserOperationResult result;
        try
        {
            result = await _userRepository.RemoveFromRoleAsync(user, roleName, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error interno al remover el rol {RoleName} del usuario {UserId}", roleName, userId);
            throw;
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning("Identity rechazó la remoción del rol {RoleName} del usuario {UserId}: {Errors}", roleName, userId, string.Join("; ", result.Errors));
        }

        return result.Succeeded;
    }
}