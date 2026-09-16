using EcoTrace.Billing.Infrastructure.Data;
using EcoTrace.Billing.Domain.Models;
using EcoTrace.Billing.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Infrastructure.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly BillingDbContext _context;

    public PaymentRepository(BillingDbContext context)
    {
        _context = context;
    }

    public Task<List<Payment>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _context.Payments.AsNoTracking().ToListAsync(cancellationToken);

    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _context.Payments.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken = default) =>
        await _context.Payments.AddAsync(payment, cancellationToken);

    public void Update(Payment payment) => _context.Payments.Update(payment);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _context.SaveChangesAsync(cancellationToken);
}
