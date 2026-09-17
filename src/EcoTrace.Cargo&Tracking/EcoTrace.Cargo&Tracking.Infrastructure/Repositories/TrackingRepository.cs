using EcoTrace.Cargo_Tracking.Domain.Models;
using EcoTrace.Cargo_Tracking.Domain.Interfaces.Repositories;
using EcoTrace.Cargo_Tracking.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Cargo_Tracking.Infrastructure.Repositories;

public class TrackingRepository : ITrackingRepository
{
    private readonly CargoTrackingDbContext _context;

    public TrackingRepository(CargoTrackingDbContext context)
    {
        _context = context;
    }

    public Task<List<TrackingSession>> GetAllAsync(Guid? cargoId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.TrackingSessions.AsNoTracking();
        if (cargoId.HasValue)
        {
            query = query.Where(t => t.CargoId == cargoId.Value);
        }
        return query.ToListAsync(cancellationToken);
    }

    public Task<TrackingSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.TrackingSessions.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<TrackingSession?> GetByCargoIdAsync(Guid cargoId, CancellationToken cancellationToken = default) =>
        _context.TrackingSessions.FirstOrDefaultAsync(t => t.CargoId == cargoId, cancellationToken);

    public async Task AddAsync(TrackingSession session, CancellationToken cancellationToken = default) =>
        await _context.TrackingSessions.AddAsync(session, cancellationToken);

    public void Update(TrackingSession session) => _context.TrackingSessions.Update(session);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}