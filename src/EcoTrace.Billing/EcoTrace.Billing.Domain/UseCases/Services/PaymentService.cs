using EcoTrace.Billing.Domain.Models;
using EcoTrace.Billing.Domain.UseCases.Contracts;
using EcoTrace.Billing.Domain.Constants;
using EcoTrace.Billing.Domain.Interfaces.Repositories;
using EcoTrace.Billing.Domain.Enumerations;
using EcoTrace.Billing.Domain.Interfaces.Services;

namespace EcoTrace.Billing.Domain.UseCases.Services;

public class PaymentService : IPaymentService
{
    // Transiciones válidas de Escrow según ADR 0001/0003: En Custodia -> Liberado | Reembolsado
    private static readonly Dictionary<EscrowStatus, EscrowStatus[]> AllowedTransitions = new()
    {
        [EscrowStatus.InCustody] = new[] { EscrowStatus.Released, EscrowStatus.Refunded },
        [EscrowStatus.Released] = Array.Empty<EscrowStatus>(),
        [EscrowStatus.Refunded] = Array.Empty<EscrowStatus>()
    };

    private readonly IPaymentRepository _repository;

    public PaymentService(IPaymentRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<PaymentResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var payments = await _repository.GetAllAsync(cancellationToken);
        return payments.Select(ToResponse).ToList();
    }

    public async Task<PaymentResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payment = await _repository.GetByIdAsync(id, cancellationToken);
        return payment is null ? null : ToResponse(payment);
    }

    public async Task<PaymentResponse> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Amount <= 0)
        {
            throw new ArgumentException(ErrorMessages.Generic.AmountMustBeGreaterThanZero);
        }

        if (request.GeneratorTenantId == request.CarrierTenantId)
        {
            throw new ArgumentException(ErrorMessages.Generic.TenantsMustBeDifferent);
        }

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            CargoId = request.CargoId,
            GeneratorTenantId = request.GeneratorTenantId,
            CarrierTenantId = request.CarrierTenantId,
            Amount = request.Amount,
            Currency = request.Currency,
            Status = EscrowStatus.InCustody,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddAsync(payment, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(payment);
    }

    public async Task<PaymentResponse?> UpdateStatusAsync(Guid id, UpdatePaymentStatusRequest request, CancellationToken cancellationToken = default)
    {
        var payment = await _repository.GetByIdAsync(id, cancellationToken);
        if (payment is null)
        {
            return null;
        }

        if (!AllowedTransitions[payment.Status].Contains(request.Status))
        {
            throw new ArgumentException(string.Format(ErrorMessages.Payment.InvalidStatusTransitionFormat, payment.Status, request.Status));
        }

        payment.Status = request.Status;
        payment.UpdatedAt = DateTime.UtcNow;

        _repository.Update(payment);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(payment);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var payment = await _repository.GetByIdAsync(id, cancellationToken);
        if (payment is null)
        {
            return false;
        }

        payment.IsDeleted = true;
        payment.DeletedAt = DateTime.UtcNow;

        _repository.Update(payment);
        await _repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static PaymentResponse ToResponse(Payment payment) => new(
        payment.Id,
        payment.CargoId,
        payment.GeneratorTenantId,
        payment.CarrierTenantId,
        payment.Amount,
        payment.Currency,
        payment.Status,
        payment.CreatedAt,
        payment.UpdatedAt
    );
}
