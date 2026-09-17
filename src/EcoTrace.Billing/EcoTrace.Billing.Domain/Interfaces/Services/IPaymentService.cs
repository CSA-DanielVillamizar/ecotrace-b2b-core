using EcoTrace.Billing.Domain.UseCases.Contracts;

namespace EcoTrace.Billing.Domain.Interfaces.Services;

public interface IPaymentService
{
    Task<List<PaymentResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PaymentResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PaymentResponse> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResponse?> UpdateStatusAsync(Guid id, UpdatePaymentStatusRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
