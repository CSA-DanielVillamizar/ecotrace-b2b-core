using EcoTrace.CargoTracking.Api.Contracts;
using EcoTrace.CargoTracking.Api.Extensions;
using EcoTrace.CargoTracking.Api.Security;
using EcoTrace.CargoTracking.Domain;
using EcoTrace.CargoTracking.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.CargoTracking.Api.Controllers;

[ApiController]
[Route("api/cargas")]
[Authorize]
[Produces("application/json")]
public sealed class CargasController(CargoTrackingDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>Crea una carga en estado Pendiente, con generador y transportista.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Supervisor + "," + Roles.Administrador)]
    [ProducesResponseType<CargaResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CargaResponse>> Crear(CrearCargaRequest solicitud, CancellationToken ct)
    {
        // Quien pide la carga es el generador: su organización sale del token.
        if (!User.PerteneceA(solicitud.GeneradorTenantId!.Value))
        {
            return Forbid();
        }

        var carga = Carga.Crear(
            solicitud.GeneradorTenantId!.Value,
            solicitud.TransportistaTenantId!.Value,
            solicitud.Descripcion,
            solicitud.Origen,
            solicitud.Destino,
            solicitud.PesoKg!.Value,
            reloj.GetUtcNow().UtcDateTime);

        db.Cargas.Add(carga);
        await db.GuardarAsync("Ya existe una carga con ese identificador.", ct);

        return CreatedAtAction(nameof(Obtener), new { id = carga.CargaId }, CargaResponse.De(carga));
    }

    /// <summary>Lista las cargas. El filtro por organización coincide con generador o transportista.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CargaResponse>>> Listar(
        [FromQuery] Guid? tenantId, [FromQuery] EstadoCarga? estado, CancellationToken ct)
    {
        // Cada organización ve las cargas en las que participa; solo el operador ve todas (lectura).
        if (!User.EsOperador())
        {
            // Un token sin organización (el de un servicio, por ejemplo) no lista nada: filtrar por "ninguna" mostraría todo.
            if (User.TenantId() is not { } propia || (tenantId is not null && tenantId != propia))
            {
                return Forbid();
            }

            tenantId = propia;
        }

        var consulta = db.Cargas.AsNoTracking();
        if (tenantId is not null)
        {
            consulta = consulta.Where(c => c.GeneradorTenantId == tenantId || c.TransportistaTenantId == tenantId);
        }

        if (estado is not null)
        {
            consulta = consulta.Where(c => c.Estado == estado);
        }

        var cargas = await consulta.OrderByDescending(c => c.CreadoEn).ToListAsync(ct);
        return cargas.Select(CargaResponse.De).ToList();
    }

    /// <summary>Devuelve la carga con su asignación y su línea de seguimiento.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CargaDetalleResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CargaDetalleResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var carga = await CargarAsync(id, tracking: false, ct);
        if (!PuedeVer(carga))
        {
            return Forbid();
        }

        return CargaDetalleResponse.De(carga);
    }

    /// <summary>Asigna vehículo y conductor (identificadores de Fleet Management). Solo desde Pendiente.</summary>
    [HttpPost("{id:guid}/asignacion")]
    [Authorize(Roles = Roles.Supervisor + "," + Roles.Administrador)]
    [ProducesResponseType<CargaDetalleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargaDetalleResponse>> Asignar(
        Guid id, AsignarCargaRequest solicitud, CancellationToken ct)
    {
        var carga = await CargarAsync(id, tracking: true, ct);
        if (!User.PerteneceA(carga.TransportistaTenantId))
        {
            return Forbid();
        }

        carga.Asignar(solicitud.VehiculoId!.Value, solicitud.ConductorId!.Value, reloj.GetUtcNow().UtcDateTime);
        await db.GuardarAsync("La carga ya tiene una asignación.", ct);

        return CreatedAtAction(nameof(Obtener), new { id }, CargaDetalleResponse.De(carga));
    }

    /// <summary>Registra un evento de seguimiento y actualiza el estado de la carga.</summary>
    [HttpPost("{id:guid}/seguimientos")]
    [Idempotente]
    [Authorize(Roles = Roles.Conductor + "," + Roles.Supervisor)]
    [ProducesResponseType<CargaDetalleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CargaDetalleResponse>> RegistrarSeguimiento(
        Guid id, RegistrarSeguimientoRequest solicitud, CancellationToken ct)
    {
        var carga = await CargarAsync(id, tracking: true, ct);

        // Quien registra el recorrido, y en especial quien confirma la entrega, es del transportista de esa carga.
        if (!User.PerteneceA(carga.TransportistaTenantId))
        {
            return Forbid();
        }

        if (solicitud.ConductorId is { } conductor && carga.Asignacion is { } asignacion && asignacion.ConductorId != conductor)
        {
            throw DomainException.Conflict("La carga fue reasignada a otro conductor mientras no había conexión.");
        }

        var ahora = reloj.GetUtcNow().UtcDateTime;
        carga.RegistrarSeguimiento(solicitud.Estado!.Value, solicitud.Ubicacion, solicitud.Nota, ahora);

        // Transactional Outbox (ADR 0002): el evento se guarda en la misma transaccion que el cambio
        // de estado. Si la entrega queda registrada, el evento tambien; si no, ninguno de los dos.
        if (carga.Estado == EstadoCarga.Entregado)
        {
            db.Outbox.Add(OutboxMensaje.EntregaConfirmada(carga, HttpContext.CorrelationId(), ahora));
        }

        await db.GuardarAsync("No se pudo registrar el seguimiento.", ct);

        return CreatedAtAction(nameof(Obtener), new { id }, CargaDetalleResponse.De(carga));
    }

    /// <summary>Línea de tiempo de la carga, del evento más antiguo al más reciente.</summary>
    [HttpGet("{id:guid}/seguimientos")]
    public async Task<ActionResult<IReadOnlyList<SeguimientoResponse>>> ListarSeguimientos(Guid id, CancellationToken ct)
    {
        var carga = await CargarAsync(id, tracking: false, ct);
        if (!PuedeVer(carga))
        {
            return Forbid();
        }

        return Ok(CargaDetalleResponse.De(carga).Seguimientos);
    }

    private bool PuedeVer(Carga carga) =>
        User.EsOperador() || User.PerteneceA(carga.GeneradorTenantId, carga.TransportistaTenantId);

    private async Task<Carga> CargarAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var consulta = db.Cargas.Include(c => c.Asignacion).Include(c => c.Seguimientos).AsQueryable();
        if (!tracking)
        {
            consulta = consulta.AsNoTracking();
        }

        return await consulta.FirstOrDefaultAsync(c => c.CargaId == id, ct)
            ?? throw DomainException.NotFound("No existe una carga con ese identificador.");
    }
}
