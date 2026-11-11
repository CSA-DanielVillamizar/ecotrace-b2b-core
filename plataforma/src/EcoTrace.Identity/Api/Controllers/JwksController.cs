using EcoTrace.Identity.Api.Seguridad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.Identity.Api.Controllers;

/// <summary>
/// Clave pública con la que se firman los tokens. Los Resource Servers la descargan una vez, la guardan
/// en memoria y solo la vuelven a pedir cuando ven un <c>kid</c> desconocido.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route(".well-known/jwks.json")]
public sealed class JwksController(ClavesDeFirma claves) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Obtener()
    {
        Response.Headers.CacheControl = "public, max-age=300";
        return Ok(claves.Jwks);
    }
}
