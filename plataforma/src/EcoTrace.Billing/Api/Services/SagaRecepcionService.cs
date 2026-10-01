using EcoTrace.Billing.Api.Contracts;
using EcoTrace.Billing.Domain;
using EcoTrace.Billing.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Api.Services;

public sealed class SagaRecepcionService(BillingDbContext db, TimeProvider reloj)
{
    public async Task<(bool Duplicado, Guid SagaId)> RecibirAsync(
        EntregaConfirmadaRequest solicitud,
        string correlationId,
        CancellationToken ct)
    {
        if (!string.Equals(solicitud.EventType, "EntregaConfirmada", StringComparison.Ordinal))
        {
            throw DomainException.Validation("eventType debe ser EntregaConfirmada.");
        }

        if (string.IsNullOrWhiteSpace(solicitud.EventId)
            || !solicitud.CargaId.HasValue || solicitud.CargaId == Guid.Empty
            || !solicitud.VehiculoId.HasValue || solicitud.VehiculoId == Guid.Empty
            || !solicitud.ConductorId.HasValue || solicitud.ConductorId == Guid.Empty
            || !solicitud.GeneradorTenantId.HasValue || solicitud.GeneradorTenantId == Guid.Empty
            || !solicitud.TransportistaTenantId.HasValue || solicitud.TransportistaTenantId == Guid.Empty)
        {
            throw DomainException.Validation("El evento necesita identificadores válidos y no vacíos.");
        }

        var eventId = solicitud.EventId.Trim();
        var existente = await db.EventosEntrega.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);
        if (existente is not null)
        {
            return (true, existente.SagaId);
        }

        var pago = await db.Pagos.FirstOrDefaultAsync(p => p.CargaId == solicitud.CargaId, ct)
            ?? throw DomainException.NotFound("No existe un pago para la carga indicada.");

        var sagaExistente = await db.Sagas.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PagoId == pago.PagoId, ct);
        if (sagaExistente is not null)
        {
            return (true, sagaExistente.SagaId);
        }

        if (pago.GeneradorTenantId != solicitud.GeneradorTenantId
            || pago.TransportistaTenantId != solicitud.TransportistaTenantId)
        {
            throw DomainException.Conflict("Los tenants del evento no coinciden con los del pago.");
        }

        if (pago.EstadoEscrow != EstadoEscrow.EnCustodia)
        {
            throw DomainException.Conflict("El pago debe estar EnCustodia para iniciar el Saga.");
        }

        var ahora = reloj.GetUtcNow().UtcDateTime;
        var saga = SagaLiberacionPago.Crear(
            pago, solicitud.VehiculoId!.Value, solicitud.ConductorId!.Value, correlationId, ahora);
        db.Sagas.Add(saga);
        db.EventosEntrega.Add(EventoEntregaConfirmada.Crear(
            eventId,
            solicitud.EventType!,
            solicitud.OccurredAt?.UtcDateTime,
            saga.SagaId,
            correlationId,
            ahora));

        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
            return (false, saga.SagaId);
        }
        catch (DbUpdateException)
        {
            await transaccion.RollbackAsync(ct);
            db.ChangeTracker.Clear();

            var eventoDuplicado = await db.EventosEntrega.AsNoTracking()
                .AnyAsync(e => e.EventId == eventId, ct);
            var sagaDuplicada = await db.Sagas.AsNoTracking()
                .FirstOrDefaultAsync(s => s.PagoId == pago.PagoId, ct);
            if (eventoDuplicado || sagaDuplicada is not null)
            {
                return (true, sagaDuplicada?.SagaId ?? Guid.Empty);
            }

            var estadoPago = await db.Pagos.AsNoTracking()
                .Where(p => p.CargaId == solicitud.CargaId)
                .Select(p => (EstadoEscrow?)p.EstadoEscrow)
                .FirstOrDefaultAsync(ct);
            if (estadoPago is not null && estadoPago != EstadoEscrow.EnCustodia)
            {
                throw DomainException.Conflict("El pago debe estar EnCustodia para iniciar el Saga.");
            }

            throw;
        }
    }

}