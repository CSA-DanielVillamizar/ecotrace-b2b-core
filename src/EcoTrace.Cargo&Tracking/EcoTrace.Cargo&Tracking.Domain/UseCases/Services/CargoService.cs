using EcoTrace.Cargo_Tracking.Domain.UseCases.Contracts;
using EcoTrace.Cargo_Tracking.Domain.Constants;
using EcoTrace.Cargo_Tracking.Domain.Enumerations;
using EcoTrace.Cargo_Tracking.Domain.Models;
using EcoTrace.Cargo_Tracking.Domain.Interfaces.Repositories;
using EcoTrace.Cargo_Tracking.Domain.Interfaces.Services;

namespace EcoTrace.Cargo_Tracking.Domain.UseCases.Services;

public class CargoService : ICargoService
{
    private readonly ICargoRepository _repository;

    public CargoService(ICargoRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<CargoResponse>> GetAllAsync(Guid? generatorTenantId = null, Guid? carrierTenantId = null, CancellationToken cancellationToken = default)
    {
        var cargos = await _repository.GetAllAsync(generatorTenantId, carrierTenantId, cancellationToken);
        return cargos.Select(ToResponse).ToList();
    }

    public async Task<CargoResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var cargo = await _repository.GetByIdAsync(id, cancellationToken);
        return cargo is null ? null : ToResponse(cargo);
    }

    public async Task<CargoResponse> CreateAsync(CreateCargoRequest request, CancellationToken cancellationToken = default)
    {
        if (request.GeneratorTenantId == request.CarrierTenantId)
        {
            throw new ArgumentException(ErrorMessages.Generic.TenantsMustBeDifferent);
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw new ArgumentException(ErrorMessages.Cargo.DescriptionRequired);
        }

        if (request.WeightKg <= 0)
        {
            throw new ArgumentException(ErrorMessages.Cargo.WeightMustBeGreaterThanZero);
        }

        if (string.IsNullOrWhiteSpace(request.OriginAddress))
        {
            throw new ArgumentException(ErrorMessages.Cargo.OriginRequired);
        }

        if (string.IsNullOrWhiteSpace(request.DestinationAddress))
        {
            throw new ArgumentException(ErrorMessages.Cargo.DestinationRequired);
        }

        var cargo = new Cargo
        {
            Id = Guid.NewGuid(),
            GeneratorTenantId = request.GeneratorTenantId,
            CarrierTenantId = request.CarrierTenantId,
            Description = request.Description.Trim(),
            WeightKg = request.WeightKg,
            OriginAddress = request.OriginAddress.Trim(),
            DestinationAddress = request.DestinationAddress.Trim(),
            Status = CargoStatus.Available,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(cargo, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(cargo);
    }

    public async Task<CargoResponse?> UpdateAsync(Guid id, UpdateCargoRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            throw new ArgumentException(ErrorMessages.Cargo.DescriptionRequired);
        }

        if (request.WeightKg <= 0)
        {
            throw new ArgumentException(ErrorMessages.Cargo.WeightMustBeGreaterThanZero);
        }

        if (string.IsNullOrWhiteSpace(request.OriginAddress))
        {
            throw new ArgumentException(ErrorMessages.Cargo.OriginRequired);
        }

        if (string.IsNullOrWhiteSpace(request.DestinationAddress))
        {
            throw new ArgumentException(ErrorMessages.Cargo.DestinationRequired);
        }

        var cargo = await _repository.GetByIdAsync(id, cancellationToken);
        if (cargo is null)
        {
            return null;
        }

        cargo.Description = request.Description.Trim();
        cargo.WeightKg = request.WeightKg;
        cargo.OriginAddress = request.OriginAddress.Trim();
        cargo.DestinationAddress = request.DestinationAddress.Trim();
        cargo.Status = request.Status;
        cargo.UpdatedAt = DateTime.UtcNow;

        _repository.Update(cargo);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(cargo);
    }

    public async Task<CargoResponse?> UpdateStatusAsync(Guid id, UpdateCargoStatusRequest request, CancellationToken cancellationToken = default)
    {
        var cargo = await _repository.GetByIdAsync(id, cancellationToken);
        if (cargo is null)
        {
            return null;
        }

        cargo.Status = request.Status;
        cargo.UpdatedAt = DateTime.UtcNow;

        _repository.Update(cargo);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(cargo);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var cargo = await _repository.GetByIdAsync(id, cancellationToken);
        if (cargo is null)
        {
            return false;
        }

        cargo.IsDeleted = true;
        cargo.DeletedAt = DateTime.UtcNow;

        _repository.Update(cargo);
        await _repository.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static CargoResponse ToResponse(Cargo c) =>
        new(c.Id, c.GeneratorTenantId, c.CarrierTenantId, c.Description, c.WeightKg, c.OriginAddress, c.DestinationAddress, c.Status, c.CreatedAt, c.UpdatedAt);
}