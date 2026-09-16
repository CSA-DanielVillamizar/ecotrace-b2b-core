using EcoTrace.Cargo_Tracking.Domain.Enumerations;

namespace EcoTrace.Cargo_Tracking.Domain.Models;

public class Cargo
{
    public Guid Id { get; set; }

    // Marketplace de dos lados (ADR 0001)
    public Guid GeneratorTenantId { get; set; }
    public Guid CarrierTenantId { get; set; }

    public string Description { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public string OriginAddress { get; set; } = string.Empty;
    public string DestinationAddress { get; set; } = string.Empty;
    public CargoStatus Status { get; set; } = CargoStatus.Created;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}