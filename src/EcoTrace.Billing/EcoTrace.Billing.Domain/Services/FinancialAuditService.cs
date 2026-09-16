using EcoTrace.Billing.Domain.Models;
using EcoTrace.Billing.Domain.DTOs;
using EcoTrace.Billing.Domain.Constants;
using EcoTrace.Billing.Domain.Repositories;

namespace EcoTrace.Billing.Domain.Services;

// Append-only por diseño (ADR 0001): sin Update/Delete.
public class FinancialAuditService : IFinancialAuditService
{
    private readonly IFinancialAuditRepository _repository;

    public FinancialAuditService(IFinancialAuditRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<FinancialAuditResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var audits = await _repository.GetAllAsync(cancellationToken);
        return audits.Select(ToResponse).ToList();
    }

    public async Task<FinancialAuditResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var audit = await _repository.GetByIdAsync(id, cancellationToken);
        return audit is null ? null : ToResponse(audit);
    }

    public async Task<FinancialAuditResponse> CreateAsync(CreateFinancialAuditRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Action))
        {
            throw new ArgumentException(ErrorMessages.FinancialAudit.ActionRequired);
        }

        var audit = new FinancialAudit
        {
            Id = Guid.NewGuid(),
            PaymentId = request.PaymentId,
            Action = request.Action,
            Details = request.Details,
            OccurredAt = DateTime.UtcNow
        };

        await _repository.AddAsync(audit, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return ToResponse(audit);
    }

    private static FinancialAuditResponse ToResponse(FinancialAudit audit) => new()
    {
        Id = audit.Id,
        PaymentId = audit.PaymentId,
        Action = audit.Action,
        Details = audit.Details,
        OccurredAt = audit.OccurredAt
    };
}
