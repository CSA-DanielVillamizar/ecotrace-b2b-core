using EcoTrace.FleetManagement.Domain.Models;
using EcoTrace.FleetManagement.Domain.Repositories;
using EcoTrace.FleetManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Infrastructure.Repositories;

public class DriverRepository : IDriverRepository
{
    private readonly FleetDbContext _context;

    public DriverRepository(FleetDbContext context)
    {
        _context = context;
    }

    public Task<List<Driver>> GetAllAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Drivers.AsNoTracking();
        if (tenantId.HasValue)
        {
            query = query.Where(d => d.TenantId == tenantId.Value);
        }
        return query.ToListAsync(cancellationToken);
    }

    public Task<Driver?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Drivers.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public Task<Driver?> GetByLicenseNumberAsync(string licenseNumber, CancellationToken cancellationToken = default) =>
        _context.Drivers.FirstOrDefaultAsync(d => d.LicenseNumber == licenseNumber, cancellationToken);

    public Task<bool> ExistsByLicenseNumberAsync(string licenseNumber, Guid? excludeId = null, CancellationToken cancellationToken = default) =>
        _context.Drivers.AnyAsync(d => d.LicenseNumber == licenseNumber && (!excludeId.HasValue || d.Id != excludeId.Value), cancellationToken);

    public async Task AddAsync(Driver driver, CancellationToken cancellationToken = default) =>
        await _context.Drivers.AddAsync(driver, cancellationToken);

    public void Update(Driver driver) => _context.Drivers.Update(driver);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}