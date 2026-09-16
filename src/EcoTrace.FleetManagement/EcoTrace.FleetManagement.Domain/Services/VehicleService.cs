using EcoTrace.FleetManagement.Domain.DTOs;
using EcoTrace.FleetManagement.Domain.Constants;
using EcoTrace.FleetManagement.Domain.Enumerations;
using EcoTrace.FleetManagement.Domain.Models;
using EcoTrace.FleetManagement.Domain.Repositories;

namespace EcoTrace.FleetManagement.Domain.Services;

public class VehicleService : IVehicleService
{
    private readonly IVehicleRepository _repository;

    public VehicleService(IVehicleRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<VehicleResponse>> GetAllAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var vehicles = await _repository.GetAllAsync(tenantId, cancellationToken);
        return vehicles.Select(ToResponse).ToList();
    }

    public async Task<VehicleResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var vehicle = await _repository.GetByIdAsync(id, cancellationToken);
        return vehicle is null ? null : ToResponse(vehicle);
    }

    public async Task<VehicleResponse> CreateAsync(CreateVehicleRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PlateNumber))
        {
            throw new ArgumentException(ErrorMessages.Vehicle.PlateNumberRequired);
        }

        if (string.IsNullOrWhiteSpace(request.Brand))
        {
            throw new ArgumentException(ErrorMessages.Vehicle.BrandRequired);
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ArgumentException(ErrorMessages.Vehicle.ModelRequired);
        }

        if (request.CapacityKg <= 0)
        {
            throw new ArgumentException(ErrorMessages.Vehicle.CapacityMustBeGreaterThanZero);
        }

        var normalizedPlate = request.PlateNumber.Trim().ToUpperInvariant();
        if (await _repository.ExistsByPlateNumberAsync(normalizedPlate, null, cancellationToken))
        {
            throw new ArgumentException(ErrorMessages.Vehicle.PlateAlreadyExists);
        }

        var vehicle = new Vehicle
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            PlateNumber = normalizedPlate,
            Brand = request.Brand.Trim(),
            Model = request.Model.Trim(),
            CapacityKg = request.CapacityKg,
            Status = VehicleStatus.Available,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(vehicle, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(vehicle);
    }

    public async Task<VehicleResponse?> UpdateAsync(Guid id, UpdateVehicleRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Brand))
        {
            throw new ArgumentException(ErrorMessages.Vehicle.BrandRequired);
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ArgumentException(ErrorMessages.Vehicle.ModelRequired);
        }

        if (request.CapacityKg <= 0)
        {
            throw new ArgumentException(ErrorMessages.Vehicle.CapacityMustBeGreaterThanZero);
        }

        var vehicle = await _repository.GetByIdAsync(id, cancellationToken);
        if (vehicle is null)
        {
            return null;
        }

        vehicle.Brand = request.Brand.Trim();
        vehicle.Model = request.Model.Trim();
        vehicle.CapacityKg = request.CapacityKg;
        vehicle.Status = request.Status;
        vehicle.UpdatedAt = DateTime.UtcNow;

        _repository.Update(vehicle);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(vehicle);
    }

    public async Task<VehicleResponse?> UpdateStatusAsync(Guid id, UpdateVehicleStatusRequest request, CancellationToken cancellationToken = default)
    {
        var vehicle = await _repository.GetByIdAsync(id, cancellationToken);
        if (vehicle is null)
        {
            return null;
        }

        vehicle.Status = request.Status;
        vehicle.UpdatedAt = DateTime.UtcNow;

        _repository.Update(vehicle);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(vehicle);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var vehicle = await _repository.GetByIdAsync(id, cancellationToken);
        if (vehicle is null)
        {
            return false;
        }

        vehicle.IsDeleted = true;
        vehicle.DeletedAt = DateTime.UtcNow;

        _repository.Update(vehicle);
        await _repository.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static VehicleResponse ToResponse(Vehicle v) =>
        new(v.Id, v.TenantId, v.PlateNumber, v.Brand, v.Model, v.CapacityKg, v.Status.ToString(), v.CreatedAt, v.UpdatedAt);
}