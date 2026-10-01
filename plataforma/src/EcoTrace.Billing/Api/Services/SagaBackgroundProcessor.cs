using EcoTrace.Billing.Domain;
using EcoTrace.Billing.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Api.Services;

public sealed class SagaBackgroundProcessor(
    IServiceScopeFactory scopeFactory,
    ILogger<SagaBackgroundProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcesarPendientesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Saga polling failed; pending Billing Sagas will be retried.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task ProcesarPendientesAsync(CancellationToken ct)
    {
        List<Guid> pendientes;
        var ahora = DateTime.UtcNow;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            pendientes = await db.Sagas.AsNoTracking()
                .Where(s => s.Estado == EstadoSaga.EnCurso || s.Estado == EstadoSaga.Compensando)
                .Where(s => s.LeaseHasta == null || s.LeaseHasta <= ahora)
                .OrderBy(s => s.CreadaEn)
                .Select(s => s.SagaId)
                .Take(20)
                .ToListAsync(ct);
        }

        foreach (var sagaId in pendientes)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var procesador = scope.ServiceProvider.GetRequiredService<SagaOrchestrator>();
            await procesador.ProcesarAsync(sagaId, ct);
        }
    }
}