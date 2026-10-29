using EcoTrace.FleetManagement.Api.Contracts;
using EcoTrace.FleetManagement.Api.Seguridad;
using EcoTrace.FleetManagement.Domain;
using EcoTrace.FleetManagement.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Api.Controllers;

/// <summary>
/// Novedades de un vehículo (Trabajo 3). Es la acción que la app del conductor encola sin señal y envía
/// cuando vuelve la conexión (ADR 0005).
/// </summary>
[ApiController]
[Authorize]
[Route("api/vehiculos/{vehiculoId:guid}/novedades")]
[Produces("application/json")]
public sealed class NovedadesController(FleetManagementDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>
    /// Reporta una novedad del vehículo. Solo personas del transportista dueño del vehículo. Es idempotente
    /// por el encabezado Idempotency-Key: si la misma acción llega otra vez (la app reintenta tras un corte de
    /// red), responde 200 con la novedad que ya se había creado y no crea otra.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = Politicas.ReportaNovedades)]
    [ProducesResponseType<NovedadResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<NovedadResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NovedadResponse>> Reportar(
        Guid vehiculoId, ReportarNovedadRequest solicitud,
        [FromHeader(Name = "Idempotency-Key")] Guid? claveIdempotencia, CancellationToken ct)
    {
        var userId = User.UserId();
        if (userId is null)
        {
            return Forbid();
        }

        var vehiculo = await BuscarVehiculoAsync(vehiculoId, ct);
        if (vehiculo.TenantId != User.TenantId())
        {
            return Forbid();
        }

        // Primero se mira si esta acción ya se procesó, antes de cualquier regla: un reintento de algo que ya
        // se aceptó tiene que recibir lo mismo que la primera vez, aunque el vehículo haya cambiado después.
        var anterior = await BuscarPorClaveAsync(claveIdempotencia, ct);
        if (anterior is not null)
        {
            return RespuestaDeReintento(anterior, vehiculoId);
        }

        var novedad = Novedad.Reportar(
            vehiculo,
            userId.Value,
            solicitud.CargaId,
            solicitud.Tipo!.Value,
            solicitud.Descripcion,
            solicitud.OcurridaEn!.Value.UtcDateTime,
            reloj.GetUtcNow().UtcDateTime,
            claveIdempotencia ?? Guid.Empty);

        db.Novedades.Add(novedad);
        try
        {
            await db.GuardarAsync("La novedad ya estaba registrada.", ct);
        }
        catch (DomainException conflicto) when (conflicto.Kind == DomainErrorKind.Conflict)
        {
            // Dos reintentos llegaron a la vez y el otro guardó primero: el índice único frenó este.
            // Se responde lo mismo que a un reintento normal.
            db.ChangeTracker.Clear();
            var ganadora = await BuscarPorClaveAsync(claveIdempotencia, ct);
            if (ganadora is null)
            {
                throw;
            }

            return RespuestaDeReintento(ganadora, vehiculoId);
        }

        return CreatedAtAction(nameof(Listar), new { vehiculoId }, NovedadResponse.De(novedad));
    }

    /// <summary>Novedades del vehículo, la más reciente primero.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NovedadResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<NovedadResponse>>> Listar(Guid vehiculoId, CancellationToken ct)
    {
        var vehiculo = await BuscarVehiculoAsync(vehiculoId, ct);
        if (!User.PuedeVer(vehiculo.TenantId))
        {
            return Forbid();
        }

        var novedades = await db.Novedades.AsNoTracking()
            .Where(n => n.VehiculoId == vehiculoId)
            .OrderByDescending(n => n.OcurridaEn)
            .ToListAsync(ct);
        return novedades.Select(NovedadResponse.De).ToList();
    }

    /// <summary>
    /// Un reintento recibe la novedad que ya existía, pero solo si es del mismo vehículo. Así nadie puede leer
    /// la novedad de otro transportista adivinando o reusando una clave.
    /// </summary>
    private ActionResult<NovedadResponse> RespuestaDeReintento(Novedad anterior, Guid vehiculoId) =>
        anterior.VehiculoId == vehiculoId
            ? Ok(NovedadResponse.De(anterior))
            : throw DomainException.Conflict("Esa Idempotency-Key ya se usó para otra acción.");

    private async Task<Novedad?> BuscarPorClaveAsync(Guid? claveIdempotencia, CancellationToken ct) =>
        claveIdempotencia is null
            ? null
            : await db.Novedades.AsNoTracking().FirstOrDefaultAsync(n => n.ClaveIdempotencia == claveIdempotencia, ct);

    private async Task<Vehiculo> BuscarVehiculoAsync(Guid vehiculoId, CancellationToken ct) =>
        await db.Vehiculos.AsNoTracking().FirstOrDefaultAsync(v => v.VehiculoId == vehiculoId, ct)
            ?? throw DomainException.NotFound("No existe un vehículo con ese identificador.");
}
