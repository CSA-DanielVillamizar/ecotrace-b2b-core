using EcoTrace.Identity.Domain.DTOs;
using EcoTrace.Identity.Domain.Constants;
using EcoTrace.Identity.Domain.Models;
using EcoTrace.Identity.Domain.Repositories;

namespace EcoTrace.Identity.Domain.Services;

public class TenantService : ITenantService
{
    private readonly ITenantRepository _repository;

    public TenantService(ITenantRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<TenantResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await _repository.GetAllAsync(cancellationToken);
        return tenants.Select(ToResponse).ToList();
    }

    public async Task<TenantResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenant = await _repository.GetByIdAsync(id, cancellationToken);
        return tenant is null ? null : ToResponse(tenant);
    }

    public async Task<TenantResponse> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException(ErrorMessages.Tenant.NameRequired);
        }

        if (string.IsNullOrWhiteSpace(request.TaxId))
        {
            throw new ArgumentException(ErrorMessages.Tenant.TaxIdRequired);
        }

        if (await _repository.ExistsByTaxIdAsync(request.TaxId.Trim(), null, cancellationToken))
        {
            throw new ArgumentException(ErrorMessages.Tenant.TaxIdAlreadyExists);
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            TaxId = request.TaxId.Trim(),
            Type = request.Type,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(tenant, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(tenant);
    }

    public async Task<TenantResponse?> UpdateAsync(Guid id, UpdateTenantRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException(ErrorMessages.Tenant.NameRequired);
        }

        if (string.IsNullOrWhiteSpace(request.TaxId))
        {
            throw new ArgumentException(ErrorMessages.Tenant.TaxIdRequired);
        }

        if (await _repository.ExistsByTaxIdAsync(request.TaxId.Trim(), id, cancellationToken))
        {
            throw new ArgumentException(ErrorMessages.Tenant.TaxIdAlreadyExists);
        }

        var tenant = await _repository.GetByIdAsync(id, cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        tenant.Name = request.Name.Trim();
        tenant.TaxId = request.TaxId.Trim();
        tenant.Type = request.Type;
        tenant.IsActive = request.IsActive;
        tenant.UpdatedAt = DateTime.UtcNow;

        _repository.Update(tenant);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(tenant);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenant = await _repository.GetByIdAsync(id, cancellationToken);
        if (tenant is null)
        {
            return false;
        }

        tenant.IsDeleted = true;
        tenant.DeletedAt = DateTime.UtcNow;

        _repository.Update(tenant);
        await _repository.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static TenantResponse ToResponse(Tenant t) =>
        new(t.Id, t.Name, t.TaxId, t.Type.ToString(), t.IsActive, t.CreatedAt, t.UpdatedAt);
}