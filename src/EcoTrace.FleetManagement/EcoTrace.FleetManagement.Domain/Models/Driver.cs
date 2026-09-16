using EcoTrace.FleetManagement.Domain.Enumerations;

namespace EcoTrace.FleetManagement.Domain.Models;

public class Driver
{
    public Guid Id { get; set; }

    // Referencia por ID al Tenant transportista (ADR 0001: bajo acoplamiento)
    public Guid TenantId { get; set; }

    // Referencia por ID al Usuario de Identity (ADR 0001)
    public Guid UserId { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string LicenseNumber { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DriverStatus Status { get; set; } = DriverStatus.Available;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}