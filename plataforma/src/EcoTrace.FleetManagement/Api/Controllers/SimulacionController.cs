using EcoTrace.FleetManagement.Api.Contracts;
using EcoTrace.FleetManagement.Api.Extensions;
using EcoTrace.FleetManagement.Domain;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.FleetManagement.Api.Controllers;

/// <summary>
/// Herramienta de demostración para el Saga. Con Simulacion:Habilitada en false (el valor por
/// defecto) estas rutas responden 404 como si no existieran.
/// </summary>
[ApiController]
[Route("api/_simulacion")]
[Produces("application/json")]
public sealed class SimulacionController(SimulacionFallos simulacion, IConfiguration configuracion) : ControllerBase
{
    private static readonly string[] Operaciones = ["liberaciones"];

    /// <summary>Hace que las próximas N llamadas de la operación respondan 503. Con 0 se restablece.</summary>
    [HttpPost("fallos")]
    public IActionResult Armar(SimularFalloRequest solicitud)
    {
        if (!Habilitada())
        {
            return NotFound();
        }

        if (!Operaciones.Contains(solicitud.Operacion!, StringComparer.OrdinalIgnoreCase))
        {
            throw DomainException.Validation($"La operación debe ser una de: {string.Join(", ", Operaciones)}.");
        }

        if (solicitud.Cantidad is < 0 or > 1000)
        {
            throw DomainException.Validation("La cantidad debe estar entre 0 y 1000.");
        }

        simulacion.Armar(solicitud.Operacion!, solicitud.Cantidad!.Value);
        return Ok(simulacion.Estado());
    }

    /// <summary>Cuántos fallos simulados le quedan a cada operación.</summary>
    [HttpGet("fallos")]
    public IActionResult Consultar() => Habilitada() ? Ok(simulacion.Estado()) : NotFound();

    private bool Habilitada() => configuracion.GetValue("Simulacion:Habilitada", false);
}
