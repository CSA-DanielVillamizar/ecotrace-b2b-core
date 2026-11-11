using EcoTrace.CargoTracking.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EcoTrace.CargoTracking.Api.Controllers;

/// <summary>
/// Lo que responde la ruta <c>/api/_meta/entorno</c>: en qué entorno corre este servicio.
/// </summary>
/// <param name="Servicio">Qué servicio responde (aquí, CargoTracking).</param>
/// <param name="Entorno">«desarrollo» o «produccion».</param>
/// <param name="Version">La versión del servicio.</param>
/// <param name="Emisor">
/// De qué emisor acepta tokens. Es distinto en cada entorno, así que si se mezclan desarrollo y producción, esto lo delata.
/// </param>
public sealed record EntornoResponse(string Servicio, string Entorno, string Version, string Emisor);

/// <summary>
/// Responde la pregunta «¿en qué entorno estás?». Sirve para comprobar el entorno <b>real</b> sin fiarse de una variable
/// de configuración: la verificación de release (ADR 0006) le pregunta al servicio en vez de solo mirar sus archivos.
/// No pide token y no revela ningún secreto.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/_meta/entorno")]
[Produces("application/json")]
public sealed class EntornoController(IHostEnvironment entorno, IOptions<OpcionesJwt> jwt) : ControllerBase
{
    /// <summary>
    /// Devuelve el nombre del servicio, el entorno, la versión y el emisor de tokens que reconoce. Cualquier entorno distinto
    /// de producción se anuncia como «desarrollo».
    /// </summary>
    [HttpGet]
    [ProducesResponseType<EntornoResponse>(StatusCodes.Status200OK)]
    public EntornoResponse Obtener() => new(
        "CargoTracking",
        entorno.IsProduction() ? "produccion" : "desarrollo",
        typeof(CargoTrackingApiMarker).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
        jwt.Value.Emisor);
}
