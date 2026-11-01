using EcoTrace.Billing.Api.Contracts;
using EcoTrace.Billing.Api.Extensions;
using EcoTrace.Billing.Api.Security;
using EcoTrace.Billing.Domain;
using EcoTrace.Billing.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Api.Controllers;

[ApiController]
[Route("api/pagos")]
[Authorize]
[Produces("application/json")]
public sealed class PagosController(BillingDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>Crea un pago y deja los fondos en custodia. Una carga admite un solo pago.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Supervisor + "," + Roles.Administrador)]
    [ProducesResponseType<PagoDetalleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PagoDetalleResponse>> Crear(CrearPagoRequest solicitud, CancellationToken ct)
    {
        // Paga el generador: su organización sale del token.
        if (!User.PerteneceA(solicitud.GeneradorTenantId!.Value))
        {
            return Forbid();
        }

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
    public async Task<ActionResult<IReadOnlyList<PagoResponse>>> Listar(
        [FromQuery] Guid? tenantId, [FromQuery] EstadoEscrow? estado, CancellationToken ct)
    {
        // Cada organización ve los pagos en los que participa; solo el operador ve todos (lectura).
        if (!User.EsOperador())
        {
            // Un token sin organización (el de un servicio, por ejemplo) no lista nada: filtrar por "ninguna" mostraría todo.
            if (User.TenantId() is not { } propia || (tenantId is not null && tenantId != propia))
            {
                return Forbid();
            }

            tenantId = propia;
        }

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
    public async Task<ActionResult<PagoDetalleResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var pago = await CargarAsync(id, tracking: false, ct);
        if (!PuedeVer(pago))
        {
            return Forbid();
        }

        return PagoDetalleResponse.De(pago);
    }

    /// <summary>Libera los fondos al transportista. Solo desde EnCustodia.</summary>
    [HttpPost("{id:guid}/liberar")]
    [Authorize(Roles = Roles.Administrador)]
    [ProducesResponseType<PagoDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PagoDetalleResponse>> Liberar(Guid id, CancellationToken ct)
    {
        var pago = await CargarAsync(id, tracking: true, ct);
        if (!User.PerteneceA(pago.GeneradorTenantId, pago.TransportistaTenantId))
        {
            return Forbid();
        }

        pago.Liberar(reloj.GetUtcNow().UtcDateTime);
        await db.GuardarAsync("No se pudo liberar el pago.", ct);

        return PagoDetalleResponse.De(pago);
    }

    /// <summary>Devuelve los fondos al generador. Solo desde EnCustodia.</summary>
    [HttpPost("{id:guid}/reembolso")]
    [Authorize(Roles = Roles.Administrador)]
    [ProducesResponseType<PagoDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PagoDetalleResponse>> Reembolsar(Guid id, CancellationToken ct)
    {
        var pago = await CargarAsync(id, tracking: true, ct);
        if (!User.PerteneceA(pago.GeneradorTenantId, pago.TransportistaTenantId))
        {
            return Forbid();
        }

        pago.Reembolsar(reloj.GetUtcNow().UtcDateTime);
        await db.GuardarAsync("No se pudo reembolsar el pago.", ct);

        return PagoDetalleResponse.De(pago);
    }

    /// <summary>
    /// Registra la evidencia de entrega de un pago. Es la acción que encola el módulo de Billing de la app: lleva
    /// <c>Idempotency-Key</c> y, si el pago ya salió de la custodia, responde 409 de tipo B.
    /// </summary>
    [HttpPost("{id:guid}/evidencias")]
    [Idempotente]
    [Authorize(Roles = Roles.Conductor + "," + Roles.Supervisor + "," + Roles.Administrador)]
    [ProducesResponseType<EvidenciaResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EvidenciaResponse>> RegistrarEvidencia(
        Guid id, RegistrarEvidenciaRequest solicitud, CancellationToken ct)
    {
        var pago = await CargarAsync(id, tracking: true, ct);
        if (!User.PerteneceA(pago.GeneradorTenantId, pago.TransportistaTenantId) || !Guid.TryParse(User.Sub(), out var quien))
        {
            return Forbid();
        }

        var evidencia = pago.RegistrarEvidencia(quien, solicitud.Descripcion, solicitud.Referencia, reloj.GetUtcNow().UtcDateTime);
        db.Evidencias.Add(evidencia);
        await db.GuardarAsync("No se pudo registrar la evidencia.", ct);

        return CreatedAtAction(nameof(Obtener), new { id }, EvidenciaResponse.De(evidencia));
    }

    /// <summary>Historial de cambios del pago, del más antiguo al más reciente.</summary>
    [HttpGet("{id:guid}/auditoria")]
    public async Task<ActionResult<IReadOnlyList<AuditoriaResponse>>> Auditoria(Guid id, CancellationToken ct)
    {
        var pago = await CargarAsync(id, tracking: false, ct);
        if (!PuedeVer(pago))
        {
            return Forbid();
        }

        return Ok(PagoDetalleResponse.De(pago).Auditoria);
    }

    private bool PuedeVer(Pago pago) =>
        User.EsOperador() || User.PerteneceA(pago.GeneradorTenantId, pago.TransportistaTenantId);

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
