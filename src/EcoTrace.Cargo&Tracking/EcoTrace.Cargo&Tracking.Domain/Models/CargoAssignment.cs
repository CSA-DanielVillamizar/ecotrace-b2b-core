using EcoTrace.Cargo_Tracking.Domain.Enumerations;

namespace EcoTrace.Cargo_Tracking.Domain.Models;

public class CargoAssignment
{
    public Guid Id { get; set; }

    public Guid CargoId { get; set; }

    // Referencias por ID al Bounded Context Fleet Management (ADR 0001)
    public Guid DriverId { get; set; }
    public Guid VehicleId { get; set; }

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Active;
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}