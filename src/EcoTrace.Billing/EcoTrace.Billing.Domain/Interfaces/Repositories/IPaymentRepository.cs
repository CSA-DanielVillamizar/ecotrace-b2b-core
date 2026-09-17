using EcoTrace.Billing.Domain.Models;

namespace EcoTrace.Billing.Domain.Interfaces.Repositories;

public interface IPaymentRepository
{
    Task<List<Payment>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);
    void Update(Payment payment);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
