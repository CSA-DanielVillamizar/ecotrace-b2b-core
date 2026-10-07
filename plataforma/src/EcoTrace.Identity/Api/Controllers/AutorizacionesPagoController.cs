using EcoTrace.Identity.Api.Contracts;
using EcoTrace.Identity.Domain;
using EcoTrace.Identity.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Identity.Api.Controllers;

/// <summary>
/// Paso 1 del Saga "Liberar Pago en Escrow" (ADR 0003) y su compensación. Billing es el
/// orquestador; Identity solo registra si la organización receptora está en condiciones de cobrar.
/// </summary>
[ApiController]
[Route("api/autorizaciones-pago")]
[Produces("application/json")]
public sealed class AutorizacionesPagoController(IdentityDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>
    /// Autoriza la liberación de un pago si el transportista está activo. Idempotente por PagoId:
    /// repetir la llamada devuelve la autorización que ya existe. Una autorización revocada no se reutiliza.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<AutorizacionPagoResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<AutorizacionPagoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AutorizacionPagoResponse>> Autorizar(AutorizarPagoRequest solicitud, CancellationToken ct)
    {
        var pagoId = solicitud.PagoId!.Value;
        var tenantId = solicitud.TenantId!.Value;

        var existente = await db.AutorizacionesPago.AsNoTracking().FirstOrDefaultAsync(a => a.PagoId == pagoId, ct);
        if (existente is not null)
        {
            return Repetida(existente, tenantId);
        }

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.TenantId == tenantId, ct)
            ?? throw DomainException.NotFound("No existe una organización con ese identificador.");

        var autorizacion = AutorizacionPago.Crear(pagoId, tenant, reloj.GetUtcNow().UtcDateTime);
        db.AutorizacionesPago.Add(autorizacion);

        try
        {
            await db.GuardarAsync("Ya existe una autorización para ese pago.", ct);
        }
        catch (DomainException conflicto) when (conflicto.Kind == DomainErrorKind.Conflict)
        {
            // Dos solicitudes simultáneas para el mismo pago: la otra ganó. Se responde con su resultado.
            db.ChangeTracker.Clear();
            var ganadora = await db.AutorizacionesPago.AsNoTracking().FirstOrDefaultAsync(a => a.PagoId == pagoId, ct);
            if (ganadora is null)
            {
                throw;
            }

            return Repetida(ganadora, tenantId);
        }

        return CreatedAtAction(nameof(Obtener), new { pagoId }, AutorizacionPagoResponse.De(autorizacion));
    }

    /// <summary>Compensación: revoca la autorización. Idempotente; revocar una ya revocada responde 200.</summary>
    [HttpPost("{pagoId:guid}/revocacion")]
    [ProducesResponseType<AutorizacionPagoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AutorizacionPagoResponse>> Revocar(Guid pagoId, CancellationToken ct)
    {
        var autorizacion = await db.AutorizacionesPago.FirstOrDefaultAsync(a => a.PagoId == pagoId, ct)
            ?? throw DomainException.NotFound("No existe una autorización para ese pago.");

        if (autorizacion.Revocar(reloj.GetUtcNow().UtcDateTime))
        {
            await db.GuardarAsync("No se pudo revocar la autorización.", ct);
        }

        return AutorizacionPagoResponse.De(autorizacion);
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

    private ActionResult<AutorizacionPagoResponse> Repetida(AutorizacionPago existente, Guid tenantId)
    {
        if (existente.Estado == EstadoAutorizacion.Revocado)
        {
            throw DomainException.Conflict("La autorización de este pago fue revocada y no puede reutilizarse.");
        }

        if (existente.TenantId != tenantId)
        {
            throw DomainException.Conflict("El pago ya fue autorizado para otra organización.");
        }

        return Ok(AutorizacionPagoResponse.De(existente));
    }
}
