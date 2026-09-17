using EcoTrace.FleetManagement.Domain.Enumerations;

namespace EcoTrace.FleetManagement.Domain.UseCases.Contracts;

public record CreateDriverRequest(
    Guid TenantId,
    Guid UserId,
    string FullName,
    string LicenseNumber,
    string? Phone
);

public record UpdateDriverRequest(
    string FullName,
    string? Phone,
    DriverStatus Status
);

public record UpdateDriverStatusRequest(
    DriverStatus Status
);

public record DriverResponse(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string FullName,
    string LicenseNumber,
    string? Phone,
    DriverStatus Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record CreateVehicleRequest(
    Guid TenantId,
    string PlateNumber,
    string Brand,
    string Model,
    decimal CapacityKg
);

public record UpdateVehicleRequest(
    string Brand,
    string Model,
    decimal CapacityKg,
    VehicleStatus Status
);

public record UpdateVehicleStatusRequest(
    VehicleStatus Status
);

public record VehicleResponse(
    Guid Id,
    Guid TenantId,
    string PlateNumber,
    string Brand,
    string Model,
    decimal CapacityKg,
    VehicleStatus Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);