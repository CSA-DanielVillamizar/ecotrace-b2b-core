using EcoTrace.Cargo_Tracking.Domain.UseCases.Contracts;

namespace EcoTrace.Cargo_Tracking.Domain.Interfaces.Services;

public interface ITrackingService
{
    Task<List<TrackingSessionResponse>> GetAllAsync(Guid? cargoId = null, CancellationToken cancellationToken = default);
    Task<TrackingSessionResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TrackingSessionResponse?> GetByCargoIdAsync(Guid cargoId, CancellationToken cancellationToken = default);
    Task<TrackingSessionResponse> StartTrackingAsync(StartTrackingRequest request, CancellationToken cancellationToken = default);
    Task<TrackingSessionResponse?> UpdateLocationAsync(Guid id, UpdateTrackingLocationRequest request, CancellationToken cancellationToken = default);
    Task<TrackingSessionResponse?> CompleteTrackingAsync(Guid id, CancellationToken cancellationToken = default);
}