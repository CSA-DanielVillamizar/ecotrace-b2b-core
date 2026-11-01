using EcoTrace.FleetManagement.Api.Contracts;
using EcoTrace.FleetManagement.Api.Extensions;
using EcoTrace.FleetManagement.Api.Security;
using EcoTrace.FleetManagement.Domain;
using EcoTrace.FleetManagement.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Api.Controllers;

[ApiController]
[Route("api/vehiculos")]
[Authorize]
[Produces("application/json")]
public sealed class VehiculosController(FleetManagementDbContext db, TimeProvider reloj) : ControllerBase
{
    /// <summary>Registra un vehículo. La placa es única en toda la plataforma.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Supervisor + "," + Roles.Administrador)]
    [ProducesResponseType<VehiculoResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VehiculoResponse>> Crear(CrearVehiculoRequest solicitud, CancellationToken ct)
    {
        // La organización y quien registra salen del token: lo que diga el cuerpo solo se acepta si coincide.
        if (!User.PerteneceA(solicitud.TenantId!.Value) || User.Sub() != solicitud.RegistradoPorUserId.ToString())
        {
            return Forbid();
        }

        var vehiculo = Vehiculo.Crear(
            solicitud.TenantId!.Value,
            solicitud.RegistradoPorUserId!.Value,
            solicitud.Placa,
            solicitud.CapacidadKg!.Value,
            reloj.GetUtcNow().UtcDateTime);

        db.Vehiculos.Add(vehiculo);
        await db.GuardarAsync("Ya existe un vehículo con esa placa.", ct);

        return CreatedAtAction(nameof(Obtener), new { id = vehiculo.VehiculoId }, VehiculoResponse.De(vehiculo));
    }

    /// <summary>Lista los vehículos, con filtro opcional por transportista.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VehiculoResponse>>> Listar([FromQuery] Guid? tenantId, CancellationToken ct)
    {
        // Cada organización ve su flota. Solo el operador de la plataforma ve la de todas (lectura).
        if (!User.EsOperador())
        {
            // Un token sin organización (el de un servicio, por ejemplo) no lista nada: filtrar por "ninguna" mostraría todo.
            if (User.TenantId() is not { } propia || (tenantId is not null && tenantId != propia))
            {
                return Forbid();
            }

            tenantId = propia;
        }

        var consulta = db.Vehiculos.AsNoTracking();
        if (tenantId is not null)
        {
            consulta = consulta.Where(v => v.TenantId == tenantId);
        }

        var vehiculos = await consulta.OrderBy(v => v.Placa).ToListAsync(ct);
        return vehiculos.Select(VehiculoResponse.De).ToList();
    }

    /// <summary>
    /// Reporta una novedad del vehículo. Es la acción que encola el módulo de Fleet de la app: lleva <c>Idempotency-Key</c> y, si el
    /// vehículo se reservó para otra carga mientras no había señal, responde 409 de tipo B.
    /// </summary>
    [HttpPost("{id:guid}/novedades")]
    [Idempotente]
    [Authorize(Roles = Roles.Conductor + "," + Roles.Supervisor + "," + Roles.Administrador)]
    [ProducesResponseType<NovedadResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<NovedadResponse>> ReportarNovedad(
        Guid id, ReportarNovedadRequest solicitud, CancellationToken ct)
    {
        var vehiculo = await db.Vehiculos.FirstOrDefaultAsync(v => v.VehiculoId == id, ct)
            ?? throw DomainException.NotFound("No existe un vehículo con ese identificador.");

        if (!User.PerteneceA(vehiculo.TenantId) || !Guid.TryParse(User.Sub(), out var quien))
        {
            return Forbid();
        }

        var novedad = vehiculo.ReportarNovedad(solicitud.CargaId!.Value, quien, solicitud.Descripcion, reloj.GetUtcNow().UtcDateTime);
        db.Novedades.Add(novedad);
        await db.GuardarAsync("No se pudo registrar la novedad.", ct);

        return CreatedAtAction(nameof(Obtener), new { id }, NovedadResponse.De(novedad));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<VehiculoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VehiculoResponse>> Obtener(Guid id, CancellationToken ct)
    {
        var vehiculo = await db.Vehiculos.AsNoTracking().FirstOrDefaultAsync(v => v.VehiculoId == id, ct)
            ?? throw DomainException.NotFound("No existe un vehículo con ese identificador.");

        if (!User.EsOperador() && !User.PerteneceA(vehiculo.TenantId))
        {
            return Forbid();
        }

        return VehiculoResponse.De(vehiculo);
    }
}
