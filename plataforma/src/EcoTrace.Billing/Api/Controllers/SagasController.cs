using EcoTrace.Billing.Api.Contracts;
using EcoTrace.Billing.Domain;
using EcoTrace.Billing.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Api.Controllers;

/// <summary>
/// Estado visible del Saga "Liberar Pago en Escrow": es la razón por la que ADR 0003 eligió
/// orquestación. En cualquier momento se puede ver en qué paso va, qué falló y qué se compensó.
/// </summary>
[ApiController]
[Route("api/sagas")]
[Produces("application/json")]
public sealed class SagasController(BillingDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<SagaResponse>> Listar(
        [FromQuery] EstadoSaga? estado, [FromQuery] Guid? pagoId, CancellationToken ct)
    {
        var consulta = db.Sagas.AsNoTracking().Include(s => s.Pasos).AsQueryable();
        if (estado is not null)
        {
            consulta = consulta.Where(s => s.Estado == estado);
        }

        if (pagoId is not null)
        {
            consulta = consulta.Where(s => s.PagoId == pagoId);
        }

        var sagas = await consulta.OrderByDescending(s => s.CreadoEn).Take(100).ToListAsync(ct);
        return sagas.Select(SagaResponse.De).ToList();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<SagaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SagaResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var saga = await db.Sagas.AsNoTracking().Include(s => s.Pasos).FirstOrDefaultAsync(s => s.SagaId == id, ct)
            ?? throw DomainException.NotFound("No existe un Saga con ese identificador.");

        return SagaResponse.De(saga);
    }
}
