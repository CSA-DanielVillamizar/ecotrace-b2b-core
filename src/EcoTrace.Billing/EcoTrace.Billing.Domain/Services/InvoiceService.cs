using EcoTrace.Billing.Domain.Models;
using EcoTrace.Billing.Domain.DTOs;
using EcoTrace.Billing.Domain.Constants;
using EcoTrace.Billing.Domain.Repositories;

namespace EcoTrace.Billing.Domain.Services;

public class InvoiceService : IInvoiceService
{
    private readonly IInvoiceRepository _repository;

    public InvoiceService(IInvoiceRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<InvoiceResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var invoices = await _repository.GetAllAsync(cancellationToken);
        return invoices.Select(ToResponse).ToList();
    }

    public async Task<InvoiceResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await _repository.GetByIdAsync(id, cancellationToken);
        return invoice is null ? null : ToResponse(invoice);
    }

    public async Task<InvoiceResponse> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
        {
            throw new ArgumentException(ErrorMessages.Generic.AmountMustBeGreaterThanZero);
        }

        if (request.GeneratorTenantId == request.CarrierTenantId)
        {
            throw new ArgumentException(ErrorMessages.Generic.TenantsMustBeDifferent);
        }

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            CargoId = request.CargoId,
            GeneratorTenantId = request.GeneratorTenantId,
            CarrierTenantId = request.CarrierTenantId,
            Amount = request.Amount,
            Currency = request.Currency,
            IssuedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(invoice, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(invoice);
    }

    public async Task<InvoiceResponse?> UpdateAsync(Guid id, UpdateInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
        {
            throw new ArgumentException(ErrorMessages.Generic.AmountMustBeGreaterThanZero);
        }

        var invoice = await _repository.GetByIdAsync(id, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        invoice.Amount = request.Amount;
        invoice.Currency = request.Currency;

        _repository.Update(invoice);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(invoice);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await _repository.GetByIdAsync(id, cancellationToken);
        if (invoice is null)
        {
            return false;
        }

        invoice.IsDeleted = true;
        invoice.DeletedAt = DateTime.UtcNow;

        _repository.Update(invoice);
        await _repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static InvoiceResponse ToResponse(Invoice invoice) => new()
    {
        Id = invoice.Id,
        CargoId = invoice.CargoId,
        GeneratorTenantId = invoice.GeneratorTenantId,
        CarrierTenantId = invoice.CarrierTenantId,
        Amount = invoice.Amount,
        Currency = invoice.Currency,
        IssuedAt = invoice.IssuedAt,
        CreatedAt = invoice.CreatedAt
    };
}
