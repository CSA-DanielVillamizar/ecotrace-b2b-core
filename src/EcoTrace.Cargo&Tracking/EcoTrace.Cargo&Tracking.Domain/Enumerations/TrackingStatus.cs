namespace EcoTrace.Cargo_Tracking.Domain.Enumerations;

public enum TrackingStatus
{
    NotStarted = 1,
    Active = 2,
    WithIssue = 3,
    Delivered = 4,
    Cancelled = 5
}