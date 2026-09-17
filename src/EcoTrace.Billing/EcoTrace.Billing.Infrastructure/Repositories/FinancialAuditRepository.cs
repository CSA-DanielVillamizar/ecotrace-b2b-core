using EcoTrace.Billing.Infrastructure.Data;
using EcoTrace.Billing.Domain.Models;
using EcoTrace.Billing.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Infrastructure.Repositories;

// Append-only por diseño (ADR 0001): sin Update/Remove.
public class FinancialAuditRepository : IFinancialAuditRepository
{
    private readonly BillingDbContext _context;

    public FinancialAuditRepository(BillingDbContext context)
    {
        _context = context;
    }

    public Task<List<FinancialAudit>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _context.FinancialAudits.AsNoTracking().ToListAsync(cancellationToken);

    public Task<FinancialAudit?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.FinancialAudits.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task AddAsync(FinancialAudit audit, CancellationToken cancellationToken = default) =>
        await _context.FinancialAudits.AddAsync(audit, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
