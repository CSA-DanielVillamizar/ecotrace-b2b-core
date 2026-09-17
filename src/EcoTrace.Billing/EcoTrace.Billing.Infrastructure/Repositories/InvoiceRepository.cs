using EcoTrace.Billing.Infrastructure.Data;
using EcoTrace.Billing.Domain.Models;
using EcoTrace.Billing.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Infrastructure.Repositories;

public class InvoiceRepository : IInvoiceRepository
{
    private readonly BillingDbContext _context;

    public InvoiceRepository(BillingDbContext context)
    {
        _context = context;
    }

    public Task<List<Invoice>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _context.Invoices.AsNoTracking().ToListAsync(cancellationToken);

    public Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Invoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default) =>
        await _context.Invoices.AddAsync(invoice, cancellationToken);

    public void Update(Invoice invoice) => _context.Invoices.Update(invoice);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
