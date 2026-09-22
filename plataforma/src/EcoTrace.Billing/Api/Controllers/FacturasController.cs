using EcoTrace.Billing.Api.Contracts;
using EcoTrace.Billing.Domain;
using EcoTrace.Billing.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Api.Controllers;

[ApiController]
[Route("api/facturas")]
[Produces("application/json")]
public sealed class FacturasController(BillingDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>Emite la factura de una carga. Una carga admite una sola factura.</summary>
    [HttpPost]
    [ProducesResponseType<FacturaResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FacturaResponse>> Emitir(EmitirFacturaRequest solicitud, CancellationToken ct)
    {
        var factura = Factura.Emitir(
            solicitud.GeneradorTenantId!.Value,
            solicitud.TransportistaTenantId!.Value,
            solicitud.CargaId!.Value,
            solicitud.Monto!.Value,
            reloj.GetUtcNow().UtcDateTime);

        db.Facturas.Add(factura);
        await db.GuardarAsync("Ya existe una factura para esa carga.", ct);

        return CreatedAtAction(nameof(Obtener), new { id = factura.FacturaId }, FacturaResponse.De(factura));
    }

    [HttpGet]
    public async Task<IReadOnlyList<FacturaResponse>> Listar([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        var consulta = db.Facturas.AsNoTracking();
        if (tenantId is not null)
        {
            consulta = consulta.Where(f => f.GeneradorTenantId == tenantId || f.TransportistaTenantId == tenantId);
        }

        var facturas = await consulta.OrderByDescending(f => f.EmitidaEn).ToListAsync(ct);
        return facturas.Select(FacturaResponse.De).ToList();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<FacturaResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FacturaResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var factura = await db.Facturas.AsNoTracking().FirstOrDefaultAsync(f => f.FacturaId == id, ct)
            ?? throw DomainException.NotFound("No existe una factura con ese identificador.");

        return FacturaResponse.De(factura);
    }
}
