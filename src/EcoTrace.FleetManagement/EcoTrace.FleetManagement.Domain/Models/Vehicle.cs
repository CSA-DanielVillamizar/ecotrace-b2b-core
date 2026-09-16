using EcoTrace.FleetManagement.Domain.Enumerations;

namespace EcoTrace.FleetManagement.Domain.Models;

public class Vehicle
{
    public Guid Id { get; set; }

    // Referencia por ID al Tenant transportista (ADR 0001)
    public Guid TenantId { get; set; }

    public string PlateNumber { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public decimal CapacityKg { get; set; }
    public VehicleStatus Status { get; set; } = VehicleStatus.Available;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}