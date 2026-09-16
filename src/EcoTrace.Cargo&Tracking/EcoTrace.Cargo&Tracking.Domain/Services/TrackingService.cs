using EcoTrace.Cargo_Tracking.Domain.DTOs;
using EcoTrace.Cargo_Tracking.Domain.Constants;
using EcoTrace.Cargo_Tracking.Domain.Enumerations;
using EcoTrace.Cargo_Tracking.Domain.Models;
using EcoTrace.Cargo_Tracking.Domain.Repositories;

namespace EcoTrace.Cargo_Tracking.Domain.Services;

public class TrackingService : ITrackingService
{
    private readonly ITrackingRepository _trackingRepository;
    private readonly ICargoRepository _cargoRepository;

    public TrackingService(
        ITrackingRepository trackingRepository,
        ICargoRepository cargoRepository)
    {
        _trackingRepository = trackingRepository;
        _cargoRepository = cargoRepository;
    }

    public async Task<List<TrackingSessionResponse>> GetAllAsync(Guid? cargoId = null, CancellationToken cancellationToken = default)
    {
        var sessions = await _trackingRepository.GetAllAsync(cargoId, cancellationToken);
        return sessions.Select(ToResponse).ToList();
    }

    public async Task<TrackingSessionResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await _trackingRepository.GetByIdAsync(id, cancellationToken);
        return session is null ? null : ToResponse(session);
    }

    public async Task<TrackingSessionResponse?> GetByCargoIdAsync(Guid cargoId, CancellationToken cancellationToken = default)
    {
        var session = await _trackingRepository.GetByCargoIdAsync(cargoId, cancellationToken);
        return session is null ? null : ToResponse(session);
    }

    public async Task<TrackingSessionResponse> StartTrackingAsync(StartTrackingRequest request, CancellationToken cancellationToken = default)
    {
        var cargo = await _cargoRepository.GetByIdAsync(request.CargoId, cancellationToken);
        if (cargo is null)
        {
            throw new ArgumentException(ErrorMessages.Cargo.CargoNotFound);
        }

        var session = new TrackingSession
        {
            Id = Guid.NewGuid(),
            CargoId = request.CargoId,
            VehicleId = request.VehicleId,
            DriverId = request.DriverId,
            Status = TrackingStatus.Active,
            StartedAt = DateTime.UtcNow,
            LastLatitude = request.InitialLatitude,
            LastLongitude = request.InitialLongitude,
            LastCheckpoint = request.InitialCheckpoint?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        cargo.Status = CargoStatus.InTransit;
        cargo.UpdatedAt = DateTime.UtcNow;

        _cargoRepository.Update(cargo);
        await _trackingRepository.AddAsync(session, cancellationToken);
        await _trackingRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(session);
    }

    public async Task<TrackingSessionResponse?> UpdateLocationAsync(Guid id, UpdateTrackingLocationRequest request, CancellationToken cancellationToken = default)
    {
        var session = await _trackingRepository.GetByIdAsync(id, cancellationToken);
        if (session is null)
        {
            return null;
        }

        if (session.Status == TrackingStatus.Delivered || session.Status == TrackingStatus.Cancelled)
        {
            throw new ArgumentException(ErrorMessages.Tracking.TrackingAlreadyCompleted);
        }

        session.LastLatitude = request.Latitude;
        session.LastLongitude = request.Longitude;
        if (!string.IsNullOrWhiteSpace(request.Checkpoint))
        {
            session.LastCheckpoint = request.Checkpoint.Trim();
        }
        if (request.Status.HasValue)
        {
            session.Status = request.Status.Value;
        }
        session.UpdatedAt = DateTime.UtcNow;

        _trackingRepository.Update(session);
        await _trackingRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(session);
    }

    public async Task<TrackingSessionResponse?> CompleteTrackingAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await _trackingRepository.GetByIdAsync(id, cancellationToken);
        if (session is null)
        {
            return null;
        }

        session.Status = TrackingStatus.Delivered;
        session.CompletedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;

        var cargo = await _cargoRepository.GetByIdAsync(session.CargoId, cancellationToken);
        if (cargo != null)
        {
            cargo.Status = CargoStatus.Delivered;
            cargo.UpdatedAt = DateTime.UtcNow;
            _cargoRepository.Update(cargo);
        }

        _trackingRepository.Update(session);
        await _trackingRepository.SaveChangesAsync(cancellationToken);

        return ToResponse(session);
    }

    private static TrackingSessionResponse ToResponse(TrackingSession t) =>
        new(t.Id, t.CargoId, t.VehicleId, t.DriverId, t.Status.ToString(), t.StartedAt, t.CompletedAt, t.LastLatitude, t.LastLongitude, t.LastCheckpoint, t.CreatedAt, t.UpdatedAt);
}