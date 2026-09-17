using EcoTrace.Identity.Domain.Models;
using EcoTrace.Identity.Domain.Interfaces.Repositories;
using EcoTrace.Identity.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Identity.Infrastructure.Repositories;

public class TenantRepository : ITenantRepository
{
    private readonly IdentityDbContext _context;

    public TenantRepository(IdentityDbContext context)
    {
        _context = context;
    }

    public Task<List<Tenant>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _context.Tenants.AsNoTracking().ToListAsync(cancellationToken);

    public Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<Tenant?> GetByTaxIdAsync(string taxId, CancellationToken cancellationToken = default) =>
        _context.Tenants.FirstOrDefaultAsync(t => t.TaxId == taxId, cancellationToken);

    public Task<bool> ExistsByTaxIdAsync(string taxId, Guid? excludeId = null, CancellationToken cancellationToken = default) =>
        _context.Tenants.AnyAsync(t => t.TaxId == taxId && (!excludeId.HasValue || t.Id != excludeId.Value), cancellationToken);

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default) =>
        await _context.Tenants.AddAsync(tenant, cancellationToken);

    public void Update(Tenant tenant) => _context.Tenants.Update(tenant);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}