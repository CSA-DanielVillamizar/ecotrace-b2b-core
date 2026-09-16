using EcoTrace.Billing.Domain.Models;
using EcoTrace.Billing.Domain.DTOs;

namespace EcoTrace.Billing.Domain.Services;

public interface IInvoiceService
{
    Task<List<InvoiceResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<InvoiceResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<InvoiceResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<InvoiceResponse?> UpdateAsync(Guid id, UpdateInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
