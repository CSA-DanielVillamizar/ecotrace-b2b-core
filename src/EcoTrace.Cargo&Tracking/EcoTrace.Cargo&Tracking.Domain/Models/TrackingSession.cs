using EcoTrace.Cargo_Tracking.Domain.Enumerations;

namespace EcoTrace.Cargo_Tracking.Domain.Models;

public class TrackingSession
{
    public Guid Id { get; set; }

    public Guid CargoId { get; set; }
    public Guid VehicleId { get; set; }
    public Guid DriverId { get; set; }

    public TrackingStatus Status { get; set; } = TrackingStatus.NotStarted;

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public double? LastLatitude { get; set; }
    public double? LastLongitude { get; set; }
    public string? LastCheckpoint { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}