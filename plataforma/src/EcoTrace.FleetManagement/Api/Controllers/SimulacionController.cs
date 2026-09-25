using EcoTrace.FleetManagement.Api.Contracts;
using EcoTrace.FleetManagement.Api.Extensions;
using EcoTrace.FleetManagement.Domain;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.FleetManagement.Api.Controllers;

/// <summary>
/// Rutas de apoyo para la demo del Saga. Solo existen si la configuración Simulacion:Habilitada es
/// true; con la configuración por defecto responden 404, porque una ruta que hace fallar el
/// servicio a voluntad no puede quedar viva en un despliegue normal.
/// </summary>
[ApiController]
[Route("api/_simulacion/fallos")]
[Produces("application/json")]
public sealed class SimulacionController(
    SimulacionDeFallos simulacion, IConfiguration configuracion) : ControllerBase
{
    private bool Habilitada => configuracion.GetValue("Simulacion:Habilitada", false);

    /// <summary>Hace que las próximas N llamadas a la operación respondan 503. Con 0 se restablece.</summary>
    [HttpPost]
    [ProducesResponseType<FallosSimuladosResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<FallosSimuladosResponse> Armar(ArmarFalloRequest solicitud)
    {
        if (!Habilitada)
        {
            return NotFound();
        }

        var operacion = (solicitud.Operacion ?? string.Empty).Trim();
        if (!operacion.Equals(SimulacionDeFallos.Liberaciones, StringComparison.OrdinalIgnoreCase))
        {
            throw DomainException.Validation(
                $"La única operación que se puede hacer fallar es {SimulacionDeFallos.Liberaciones}.");
        }

        simulacion.Armar(SimulacionDeFallos.Liberaciones, solicitud.Cantidad!.Value);
        return Ok(new FallosSimuladosResponse(simulacion.Restantes()));
    }

    /// <summary>Cuántos fallos le quedan a cada operación.</summary>
    [HttpGet]
    [ProducesResponseType<FallosSimuladosResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<FallosSimuladosResponse> Consultar() =>
        Habilitada ? Ok(new FallosSimuladosResponse(simulacion.Restantes())) : NotFound();
}
