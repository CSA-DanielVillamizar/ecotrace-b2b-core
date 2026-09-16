using EcoTrace.Cargo_Tracking.Domain.Models;
using EcoTrace.Cargo_Tracking.Domain.Repositories;
using EcoTrace.Cargo_Tracking.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Cargo_Tracking.Infrastructure.Repositories;

public class CargoRepository : ICargoRepository
{
    private readonly CargoTrackingDbContext _context;

    public CargoRepository(CargoTrackingDbContext context)
    {
        _context = context;
    }

    public Task<List<Cargo>> GetAllAsync(Guid? generatorTenantId = null, Guid? carrierTenantId = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Cargoes.AsNoTracking();
        if (generatorTenantId.HasValue)
        {
            query = query.Where(c => c.GeneratorTenantId == generatorTenantId.Value);
        }
        if (carrierTenantId.HasValue)
        {
            query = query.Where(c => c.CarrierTenantId == carrierTenantId.Value);
        }
        return query.ToListAsync(cancellationToken);
    }

    public Task<Cargo?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Cargoes.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task AddAsync(Cargo cargo, CancellationToken cancellationToken = default) =>
        await _context.Cargoes.AddAsync(cargo, cancellationToken);

    public void Update(Cargo cargo) => _context.Cargoes.Update(cargo);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}