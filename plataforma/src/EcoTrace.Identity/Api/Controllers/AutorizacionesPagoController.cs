using EcoTrace.Identity.Api.Contracts;
using EcoTrace.Identity.Domain;
using EcoTrace.Identity.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Identity.Api.Controllers;

/// <summary>Paso 1 del Saga Liberar Pago en Escrow (ADR 0003 / Trabajo 2): autoriza y revoca pagos.</summary>
[ApiController]
[Route("api/autorizaciones-pago")]
[Produces("application/json")]
public sealed class AutorizacionesPagoController(IdentityDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>
    /// Autoriza un pago. Si dos solicitudes llegan a la vez para el mismo PagoId, la que pierde
    /// la carrera responde 200 con el resultado de la que ganó, no con un error.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<AutorizacionPagoResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<AutorizacionPagoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AutorizacionPagoResponse>> Autorizar(AutorizarPagoRequest solicitud, CancellationToken ct)
    {
        var pagoId = solicitud.PagoId!.Value;
        var tenantId = solicitud.TenantId!.Value;

        var existente = await db.AutorizacionesPago.AsNoTracking().FirstOrDefaultAsync(a => a.PagoId == pagoId, ct);
        if (existente is not null)
        {
            return ResultadoParaExistente(existente, tenantId);
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId, ct)
            ?? throw DomainException.NotFound("No existe una organización con ese identificador.");

        var autorizacion = AutorizacionPago.Autorizar(pagoId, tenant, reloj.GetUtcNow().UtcDateTime);
        db.AutorizacionesPago.Add(autorizacion);

        if (!await db.IntentarGuardarAsync(ct))
        {
            // Perdimos la carrera: otra solicitud concurrente ya autorizó este mismo pago.
            var ganadora = await db.AutorizacionesPago.AsNoTracking().FirstAsync(a => a.PagoId == pagoId, ct);
            return ResultadoParaExistente(ganadora, tenantId);
        }

        return CreatedAtAction(nameof(Obtener), new { pagoId }, AutorizacionPagoResponse.De(autorizacion));
    }

    [HttpGet("{pagoId:guid}")]
    [ProducesResponseType<AutorizacionPagoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AutorizacionPagoResponse>> Obtener(Guid pagoId, CancellationToken ct)
    {
        var autorizacion = await db.AutorizacionesPago.AsNoTracking().FirstOrDefaultAsync(a => a.PagoId == pagoId, ct)
            ?? throw DomainException.NotFound("No existe una autorización para ese pago.");

        return AutorizacionPagoResponse.De(autorizacion);
    }

    /// <summary>Idempotente: repetirla responde 200. 404 si no hay autorización para ese pago.</summary>
    [HttpPost("{pagoId:guid}/revocacion")]
    [ProducesResponseType<AutorizacionPagoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AutorizacionPagoResponse>> Revocar(Guid pagoId, CancellationToken ct)
    {
        var autorizacion = await db.AutorizacionesPago.FirstOrDefaultAsync(a => a.PagoId == pagoId, ct)
            ?? throw DomainException.NotFound("No existe una autorización para ese pago.");

        autorizacion.Revocar(reloj.GetUtcNow().UtcDateTime);
        await db.GuardarAsync("La autorización cambió mientras se procesaba la solicitud.", ct);

        return AutorizacionPagoResponse.De(autorizacion);
    }

    private ActionResult<AutorizacionPagoResponse> ResultadoParaExistente(AutorizacionPago existente, Guid tenantIdSolicitado)
    {
        if (existente.Estado == EstadoAutorizacion.Revocado)
        {
            throw DomainException.Conflict("La autorización de ese pago fue revocada.");
        }

        if (existente.TenantId != tenantIdSolicitado)
        {
            throw DomainException.Conflict("El pago ya fue autorizado para otra organización.");
        }

        return Ok(AutorizacionPagoResponse.De(existente));
    }
}
