using EcoTrace.Cargo_Tracking.Domain.Models;
using EcoTrace.Cargo_Tracking.Domain.Repositories;
using EcoTrace.Cargo_Tracking.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Cargo_Tracking.Infrastructure.Repositories;

public class CargoAssignmentRepository : ICargoAssignmentRepository
{
    private readonly CargoTrackingDbContext _context;

    public CargoAssignmentRepository(CargoTrackingDbContext context)
    {
        _context = context;
    }

    public Task<List<CargoAssignment>> GetAllAsync(Guid? cargoId = null, Guid? driverId = null, Guid? vehicleId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.CargoAssignments.AsNoTracking();
        if (cargoId.HasValue)
        {
            query = query.Where(ca => ca.CargoId == cargoId.Value);
        }
        if (driverId.HasValue)
        {
            query = query.Where(ca => ca.DriverId == driverId.Value);
        }
        if (vehicleId.HasValue)
        {
            query = query.Where(ca => ca.VehicleId == vehicleId.Value);
        }
        return query.ToListAsync(cancellationToken);
    }

    public Task<CargoAssignment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.CargoAssignments.FirstOrDefaultAsync(ca => ca.Id == id, cancellationToken);

    public Task<CargoAssignment?> GetActiveByCargoIdAsync(Guid cargoId, CancellationToken cancellationToken = default) =>
        _context.CargoAssignments.FirstOrDefaultAsync(ca => ca.CargoId == cargoId && ca.Status == Domain.Enumerations.AssignmentStatus.Active, cancellationToken);

    public async Task AddAsync(CargoAssignment assignment, CancellationToken cancellationToken = default) =>
        await _context.CargoAssignments.AddAsync(assignment, cancellationToken);

    public void Update(CargoAssignment assignment) => _context.CargoAssignments.Update(assignment);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}