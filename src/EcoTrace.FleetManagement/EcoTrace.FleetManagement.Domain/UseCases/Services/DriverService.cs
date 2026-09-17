using EcoTrace.FleetManagement.Domain.UseCases.Contracts;
using EcoTrace.FleetManagement.Domain.Constants;
using EcoTrace.FleetManagement.Domain.Enumerations;
using EcoTrace.FleetManagement.Domain.Models;
using EcoTrace.FleetManagement.Domain.Interfaces.Repositories;
using EcoTrace.FleetManagement.Domain.Interfaces.Services;

namespace EcoTrace.FleetManagement.Domain.UseCases.Services;

public class DriverService : IDriverService
{
    private readonly IDriverRepository _repository;

    public DriverService(IDriverRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<DriverResponse>> GetAllAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        var drivers = await _repository.GetAllAsync(tenantId, cancellationToken);
        return drivers.Select(ToResponse).ToList();
    }

    public async Task<DriverResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var driver = await _repository.GetByIdAsync(id, cancellationToken);
        return driver is null ? null : ToResponse(driver);
    }

    public async Task<DriverResponse> CreateAsync(CreateDriverRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ArgumentException(ErrorMessages.Driver.FullNameRequired);
        }

        if (string.IsNullOrWhiteSpace(request.LicenseNumber))
        {
            throw new ArgumentException(ErrorMessages.Driver.LicenseNumberRequired);
        }

        if (await _repository.ExistsByLicenseNumberAsync(request.LicenseNumber.Trim(), null, cancellationToken))
        {
            throw new ArgumentException(ErrorMessages.Driver.LicenseAlreadyExists);
        }

        var driver = new Driver
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            UserId = request.UserId,
            FullName = request.FullName.Trim(),
            LicenseNumber = request.LicenseNumber.Trim(),
            Phone = request.Phone?.Trim(),
            Status = DriverStatus.Available,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(driver, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(driver);
    }

    public async Task<DriverResponse?> UpdateAsync(Guid id, UpdateDriverRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            throw new ArgumentException(ErrorMessages.Driver.FullNameRequired);
        }

        var driver = await _repository.GetByIdAsync(id, cancellationToken);
        if (driver is null)
        {
            return null;
        }

        driver.FullName = request.FullName.Trim();
        driver.Phone = request.Phone?.Trim();
        driver.Status = request.Status;
        driver.UpdatedAt = DateTime.UtcNow;

        _repository.Update(driver);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(driver);
    }

    public async Task<DriverResponse?> UpdateStatusAsync(Guid id, UpdateDriverStatusRequest request, CancellationToken cancellationToken = default)
    {
        var driver = await _repository.GetByIdAsync(id, cancellationToken);
        if (driver is null)
        {
            return null;
        }

        driver.Status = request.Status;
        driver.UpdatedAt = DateTime.UtcNow;

        _repository.Update(driver);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(driver);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var driver = await _repository.GetByIdAsync(id, cancellationToken);
        if (driver is null)
        {
            return false;
        }

        driver.IsDeleted = true;
        driver.DeletedAt = DateTime.UtcNow;

        _repository.Update(driver);
        await _repository.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static DriverResponse ToResponse(Driver d) =>
        new(d.Id, d.TenantId, d.UserId, d.FullName, d.LicenseNumber, d.Phone, d.Status, d.CreatedAt, d.UpdatedAt);
}