namespace EcoTrace.Cargo_Tracking.Domain.Enumerations;

public enum CargoStatus
{
    Created = 1,
    Available = 2,
    Reserved = 3,
    Assigned = 4,
    InTransit = 5,
    Delivered = 6,
    Cancelled = 7
}