using EcoTrace.Cargo_Tracking.Domain.Enumerations;

namespace EcoTrace.Cargo_Tracking.Domain.UseCases.Contracts;

public record CreateCargoRequest(
    Guid GeneratorTenantId,
    Guid CarrierTenantId,
    string Description,
    decimal WeightKg,
    string OriginAddress,
    string DestinationAddress
);

public record UpdateCargoRequest(
    string Description,
    decimal WeightKg,
    string OriginAddress,
    string DestinationAddress,
    CargoStatus Status
);

public record UpdateCargoStatusRequest(
    CargoStatus Status
);

public record CargoResponse(
    Guid Id,
    Guid GeneratorTenantId,
    Guid CarrierTenantId,
    string Description,
    decimal WeightKg,
    string OriginAddress,
    string DestinationAddress,
    CargoStatus Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record CreateCargoAssignmentRequest(
    Guid CargoId,
    Guid DriverId,
    Guid VehicleId,
    string? Notes
);

public record UpdateAssignmentStatusRequest(
    AssignmentStatus Status
);

public record CargoAssignmentResponse(
    Guid Id,
    Guid CargoId,
    Guid DriverId,
    Guid VehicleId,
    DateTime AssignedAt,
    AssignmentStatus Status,
    string? Notes,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record StartTrackingRequest(
    Guid CargoId,
    Guid VehicleId,
    Guid DriverId,
    double? InitialLatitude,
    double? InitialLongitude,
    string? InitialCheckpoint
);

public record UpdateTrackingLocationRequest(
    double Latitude,
    double Longitude,
    string? Checkpoint,
    TrackingStatus? Status
);

public record TrackingSessionResponse(
    Guid Id,
    Guid CargoId,
    Guid VehicleId,
    Guid DriverId,
    TrackingStatus Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    double? LastLatitude,
    double? LastLongitude,
    string? LastCheckpoint,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);