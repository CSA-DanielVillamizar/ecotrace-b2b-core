using EcoTrace.Billing.Api.Contracts;
using EcoTrace.Billing.Domain;
using EcoTrace.Billing.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Api.Controllers;

[ApiController]
[Route("api/pagos")]
[Produces("application/json")]
public sealed class PagosController(BillingDbContext db, TimeProvider reloj) : ControllerBase
{
    private static readonly Guid OperadorTenantId = Guid.Parse("0e7ace00-0000-4000-8000-000000000001");

    /// <summary>Crea un pago y deja los fondos en custodia. Una carga admite un solo pago.</summary>
    [HttpPost]
    [Authorize(Policy = "CrearPago")]
    [ProducesResponseType<PagoDetalleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagoDetalleResponse>> Crear(CrearPagoRequest solicitud, CancellationToken ct)
    {
        var tenantId = ObtenerTenantId();
        if (tenantId is not null && tenantId != OperadorTenantId && solicitud.GeneradorTenantId != tenantId)
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
    [Authorize(Policy = "Lectura")]
    public async Task<IReadOnlyList<PagoResponse>> Listar(
        [FromQuery] Guid? tenantId, [FromQuery] EstadoEscrow? estado, CancellationToken ct)
    {
        var miTenant = ObtenerTenantId();
        var consulta = db.Pagos.AsNoTracking();

        if (miTenant is not null && miTenant != OperadorTenantId)
        {
            consulta = consulta.Where(p => p.GeneradorTenantId == miTenant || p.TransportistaTenantId == miTenant);
        }
        else if (tenantId is not null)
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
    [Authorize(Policy = "Lectura")]
    [ProducesResponseType<PagoDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagoDetalleResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var pago = await CargarAsync(id, tracking: false, ct);
        if (!TieneAcceso(pago))
        {
            return Forbid();
        }

        return PagoDetalleResponse.De(pago);
    }

    /// <summary>Libera los fondos al transportista. Solo desde EnCustodia.</summary>
    [HttpPost("{id:guid}/liberar")]
    [Authorize(Policy = "Administrador")]
    [ProducesResponseType<PagoDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagoDetalleResponse>> Liberar(Guid id, CancellationToken ct)
    {
        var pago = await CargarAsync(id, tracking: true, ct);
        if (!TieneAcceso(pago))
        {
            return Forbid();
        }

        pago.Liberar(reloj.GetUtcNow().UtcDateTime);
        await db.GuardarAsync("No se pudo liberar el pago.", ct);

        return PagoDetalleResponse.De(pago);
    }

    /// <summary>Devuelve los fondos al generador. Solo desde EnCustodia.</summary>
    [HttpPost("{id:guid}/reembolso")]
    [Authorize(Policy = "Administrador")]
    [ProducesResponseType<PagoDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagoDetalleResponse>> Reembolsar(Guid id, CancellationToken ct)
    {
        var pago = await CargarAsync(id, tracking: true, ct);
        if (!TieneAcceso(pago))
        {
            return Forbid();
        }

        pago.Reembolsar(reloj.GetUtcNow().UtcDateTime);
        await db.GuardarAsync("No se pudo reembolsar el pago.", ct);

        return PagoDetalleResponse.De(pago);
    }

    /// <summary>Historial de cambios del pago, del más antiguo al más reciente.</summary>
    [HttpGet("{id:guid}/auditoria")]
    [Authorize(Policy = "Lectura")]
    [ProducesResponseType<IReadOnlyList<AuditoriaResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AuditoriaResponse>>> Auditoria(Guid id, CancellationToken ct)
    {
        var pago = await CargarAsync(id, tracking: false, ct);
        if (!TieneAcceso(pago))
        {
            return Forbid();
        }

        return Ok(PagoDetalleResponse.De(pago).Auditoria);
    }

    /// <summary>Registra evidencia de entrega desde la app móvil con idempotencia (ADR 0005).</summary>
    [HttpPost("{id:guid}/evidencias")]
    [Authorize(Policy = "Lectura")]
    [ProducesResponseType<PagoDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagoDetalleResponse>> RegistrarEvidencia(
        Guid id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        RegistrarEvidenciaRequest solicitud,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Falta encabezado",
                Detail = "Se requiere el encabezado Idempotency-Key."
            });
        }

        var pago = await CargarAsync(id, tracking: true, ct);
        if (!TieneAcceso(pago))
        {
            return Forbid();
        }

        // Idempotencia: si la operación ya se procesó, respondemos 200 con el estado actual sin duplicar efecto.
        var operacionExistente = await db.OperacionesPago.FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey, ct);
        if (operacionExistente is not null)
        {
            return Ok(PagoDetalleResponse.De(pago));
        }

        // Conflicto Tipo B: si el pago ya fue liberado o reembolsado mientras el dispositivo estaba offline.
        if (pago.EstadoEscrow != EstadoEscrow.EnCustodia)
        {
            var conflicto = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflicto de sincronización",
                Detail = "El pago ya fue liberado por otro proceso."
            };
            conflicto.Extensions["tipoConflicto"] = "B";
            return new ObjectResult(conflicto) { StatusCode = StatusCodes.Status409Conflict };
        }

        pago.RegistrarEvento($"Evidencia registrada: {solicitud.Evidencia}", reloj.GetUtcNow().UtcDateTime);
        db.OperacionesPago.Add(OperacionPago.Registrar(pago.PagoId, idempotencyKey, "RegistrarEvidencia", reloj.GetUtcNow().UtcDateTime));
        await db.GuardarAsync("No se pudo registrar la evidencia del pago.", ct);

        return Ok(PagoDetalleResponse.De(pago));
    }

    private Guid? ObtenerTenantId() =>
        Guid.TryParse(User.FindFirst("tenant_id")?.Value, out var tenantId) ? tenantId : null;

    private bool TieneAcceso(Pago pago)
    {
        var tenantId = ObtenerTenantId();
        if (tenantId is null)
        {
            return false;
        }

        return tenantId == OperadorTenantId || pago.GeneradorTenantId == tenantId || pago.TransportistaTenantId == tenantId;
    }

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
