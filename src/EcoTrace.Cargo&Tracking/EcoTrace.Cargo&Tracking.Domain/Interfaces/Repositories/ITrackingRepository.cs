using EcoTrace.Cargo_Tracking.Domain.Models;

namespace EcoTrace.Cargo_Tracking.Domain.Interfaces.Repositories;

public interface ITrackingRepository
{
    Task<List<TrackingSession>> GetAllAsync(Guid? cargoId = null, CancellationToken cancellationToken = default);
    Task<TrackingSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TrackingSession?> GetByCargoIdAsync(Guid cargoId, CancellationToken cancellationToken = default);
    Task AddAsync(TrackingSession session, CancellationToken cancellationToken = default);
    void Update(TrackingSession session);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}