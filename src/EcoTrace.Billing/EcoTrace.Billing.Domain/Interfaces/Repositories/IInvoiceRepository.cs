using EcoTrace.Billing.Domain.Models;

namespace EcoTrace.Billing.Domain.Interfaces.Repositories;

public interface IInvoiceRepository
{
    Task<List<Invoice>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default);
    void Update(Invoice invoice);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
