using EcoTrace.Billing.Api.Contracts;
using EcoTrace.Billing.Domain;
using EcoTrace.Billing.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Api.Controllers;

[ApiController]
[Route("api/pagos")]
[Produces("application/json")]
public sealed class PagosController(BillingDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>Crea un pago y deja los fondos en custodia. Una carga admite un solo pago.</summary>
    [HttpPost]
    [ProducesResponseType<PagoDetalleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PagoDetalleResponse>> Crear(CrearPagoRequest solicitud, CancellationToken ct)
    {
        var pago = Pago.Crear(
            solicitud.GeneradorTenantId!.Value,
            solicitud.TransportistaTenantId!.Value,
            solicitud.CargaId!.Value,
            solicitud.Monto!.Value,
            reloj.GetUtcNow().UtcDateTime);

        db.Pagos.Add(pago);
        await db.GuardarAsync("Ya existe un pago en Escrow para esa carga.", ct);

        return CreatedAtAction(nameof(Obtener), new { id = pago.PagoId }, PagoDetalleResponse.De(pago));
    }

    /// <summary>Lista los pagos. El filtro por organización coincide con generador o transportista.</summary>
    [HttpGet]
    public async Task<IReadOnlyList<PagoResponse>> Listar(
        [FromQuery] Guid? tenantId, [FromQuery] EstadoEscrow? estado, CancellationToken ct)
    {
        var consulta = db.Pagos.AsNoTracking();
        if (tenantId is not null)
        {
            consulta = consulta.Where(p => p.GeneradorTenantId == tenantId || p.TransportistaTenantId == tenantId);
        }

        if (estado is not null)
        {
            consulta = consulta.Where(p => p.EstadoEscrow == estado);
        }

        var pagos = await consulta.OrderByDescending(p => p.CreadoEn).ToListAsync(ct);
        return pagos.Select(PagoResponse.De).ToList();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PagoDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagoDetalleResponse>> Obtener(Guid id, CancellationToken ct) =>
        PagoDetalleResponse.De(await CargarAsync(id, tracking: false, ct));

    /// <summary>Libera los fondos al transportista. Solo desde EnCustodia.</summary>
    [HttpPost("{id:guid}/liberar")]
    [ProducesResponseType<PagoDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PagoDetalleResponse>> Liberar(Guid id, CancellationToken ct)
    {
        var pago = await CargarAsync(id, tracking: true, ct);
        pago.Liberar(reloj.GetUtcNow().UtcDateTime);
        await db.GuardarAsync("No se pudo liberar el pago.", ct);

        return PagoDetalleResponse.De(pago);
    }

    /// <summary>Devuelve los fondos al generador. Solo desde EnCustodia.</summary>
    [HttpPost("{id:guid}/reembolso")]
    [ProducesResponseType<PagoDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PagoDetalleResponse>> Reembolsar(Guid id, CancellationToken ct)
    {
        var pago = await CargarAsync(id, tracking: true, ct);
        pago.Reembolsar(reloj.GetUtcNow().UtcDateTime);
        await db.GuardarAsync("No se pudo reembolsar el pago.", ct);

        return PagoDetalleResponse.De(pago);
    }

    /// <summary>Historial de cambios del pago, del más antiguo al más reciente.</summary>
    [HttpGet("{id:guid}/auditoria")]
    public async Task<IReadOnlyList<AuditoriaResponse>> Auditoria(Guid id, CancellationToken ct) =>
        PagoDetalleResponse.De(await CargarAsync(id, tracking: false, ct)).Auditoria;

    private async Task<Pago> CargarAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var consulta = db.Pagos.Include(p => p.Auditoria).AsQueryable();
        if (!tracking)
        {
            consulta = consulta.AsNoTracking();
        }

        return await consulta.FirstOrDefaultAsync(p => p.PagoId == id, ct)
            ?? throw DomainException.NotFound("No existe un pago con ese identificador.");
    }
}
