using EcoTrace.Identity.Domain.DTOs;
using EcoTrace.Identity.Domain.Constants;
using EcoTrace.Identity.Domain.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EcoTrace.Identity.Domain.Services;

public class RoleService : IRoleService
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ILogger<RoleService> _logger;

    public RoleService(RoleManager<ApplicationRole> roleManager, ILogger<RoleService> logger)
    {
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task<List<RoleResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _roleManager.Roles.AsNoTracking().ToListAsync(cancellationToken);
        return roles.Select(r => new RoleResponse(r.Id, r.Name ?? string.Empty, r.Description, r.CreatedAt)).ToList();
    }

    public async Task<RoleResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return role is null ? null : new RoleResponse(role.Id, role.Name ?? string.Empty, role.Description, role.CreatedAt);
    }

    public async Task<RoleResponse> CreateAsync(CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException(ErrorMessages.Role.RoleNameRequired);
        }

        var normalizedName = request.Name.Trim();
        if (await _roleManager.RoleExistsAsync(normalizedName))
        {
            throw new ArgumentException(ErrorMessages.Role.RoleAlreadyExists);
        }

        var role = new ApplicationRole(normalizedName, request.Description?.Trim())
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };

        IdentityResult result;
        try
        {
            result = await _roleManager.CreateAsync(role);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Error interno al crear el rol {RoleName}", normalizedName);
            throw;
        }

        if (!result.Succeeded)
        {
            _logger.LogWarning("Identity rechazó la creación del rol {RoleName}: {Errors}", normalizedName, string.Join("; ", result.Errors.Select(e => e.Code)));
            throw new ArgumentException("No se pudo crear el rol.");
        }

        return new RoleResponse(role.Id, role.Name ?? string.Empty, role.Description, role.CreatedAt);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(id.ToString());
        if (role is null)
        {
            return false;
        }

        var result = await _roleManager.DeleteAsync(role);
        return result.Succeeded;
    }
}