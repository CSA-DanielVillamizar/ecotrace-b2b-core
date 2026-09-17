namespace EcoTrace.FleetManagement.Domain;
using System;

public class Driver
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string FullName { get; private set; }
    public string LicenseNumber { get; private set; }
    public bool IsAvailable { get; private set; }

    protected Driver() { }

    public Driver(Guid tenantId, string fullName, string licenseNumber)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        FullName = fullName;
        LicenseNumber = licenseNumber;
        IsAvailable = true;
    }
}