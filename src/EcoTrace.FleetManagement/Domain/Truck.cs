namespace EcoTrace.FleetManagement.Domain;
using System;

public class Truck
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string LicensePlate { get; private set; }
    public string Model { get; private set; }
    public double CapacityTons { get; private set; }

    protected Truck() { }

    public Truck(Guid tenantId, string licensePlate, string model, double capacityTons)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        LicensePlate = licensePlate;
        Model = model;
        CapacityTons = capacityTons;
    }
}