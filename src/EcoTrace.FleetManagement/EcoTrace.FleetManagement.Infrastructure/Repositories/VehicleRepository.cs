using EcoTrace.FleetManagement.Domain.Models;
using EcoTrace.FleetManagement.Domain.Interfaces.Repositories;
using EcoTrace.FleetManagement.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Infrastructure.Repositories;

public class VehicleRepository : IVehicleRepository
{
    private readonly FleetDbContext _context;

    public VehicleRepository(FleetDbContext context)
    {
        _context = context;
    }

    public Task<List<Vehicle>> GetAllAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Vehicles.AsNoTracking();
        if (tenantId.HasValue)
        {
            query = query.Where(v => v.TenantId == tenantId.Value);
        }
        return query.ToListAsync(cancellationToken);
    }

    public Task<Vehicle?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Vehicles.FirstOrDefaultAsync(v => v.Id == id, cancellationToken);

    public Task<Vehicle?> GetByPlateNumberAsync(string plateNumber, CancellationToken cancellationToken = default) =>
        _context.Vehicles.FirstOrDefaultAsync(v => v.PlateNumber == plateNumber, cancellationToken);

    public Task<bool> ExistsByPlateNumberAsync(string plateNumber, Guid? excludeId = null, CancellationToken cancellationToken = default) =>
        _context.Vehicles.AnyAsync(v => v.PlateNumber == plateNumber && (!excludeId.HasValue || v.Id != excludeId.Value), cancellationToken);

    public async Task AddAsync(Vehicle vehicle, CancellationToken cancellationToken = default) =>
        await _context.Vehicles.AddAsync(vehicle, cancellationToken);

    public void Update(Vehicle vehicle) => _context.Vehicles.Update(vehicle);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}