using EcoTrace.Identity.Domain.Enumerations;

namespace EcoTrace.Identity.Domain.DTOs;

public record CreateTenantRequest(
    string Name,
    string TaxId,
    TenantType Type
);

public record UpdateTenantRequest(
    string Name,
    string TaxId,
    TenantType Type,
    bool IsActive
);

public record TenantResponse(
    Guid Id,
    string Name,
    string TaxId,
    string Type,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record CreateUserRequest(
    Guid TenantId,
    string FullName,
    string Email,
    string Password,
    string? PhoneNumber,
    List<string>? Roles
);

public record UpdateUserRequest(
    string FullName,
    string? PhoneNumber,
    bool IsActive
);

public record UserResponse(
    Guid Id,
    Guid TenantId,
    string FullName,
    string Email,
    string? PhoneNumber,
    bool IsActive,
    List<string> Roles,
    DateTime CreatedAt
);

public record CreateRoleRequest(
    string Name,
    string? Description
);

public record RoleResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTime CreatedAt
);

public record AssignRoleRequest(
    string RoleName
);