using EcoTrace.Identity.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.Identity.Api.Extensions;

/// <summary>
/// Traduce las DomainException a respuestas ProblemDetails (RFC 9457) y decide con que nivel se
/// registra cada excepcion: una regla de negocio rechazada es informacion, un fallo inesperado
/// es un error. El middleware de ASP.NET 8 registra ambos como error, por eso su categoria se
/// silencia en ApiDefaults y este manejador asume el registro.
/// </summary>
internal sealed class DomainExceptionHandler(
    IProblemDetailsService problemas, ILogger<DomainExceptionHandler> registro) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception excepcion, CancellationToken ct)
    {
        if (excepcion is not DomainException dominio)
        {
            registro.LogError(excepcion, "Excepción no controlada en {Metodo} {Ruta}", http.Request.Method, http.Request.Path);
            return false;
        }

        var (estado, titulo) = dominio.Kind switch
        {
            DomainErrorKind.Validation => (StatusCodes.Status400BadRequest, "El dato no es válido"),
            DomainErrorKind.Conflict => (StatusCodes.Status409Conflict, "La operación choca con el estado actual"),
            DomainErrorKind.NotFound => (StatusCodes.Status404NotFound, "El recurso no existe"),
            _ => (StatusCodes.Status500InternalServerError, "Error inesperado")
        };

        registro.LogInformation(
            "Regla de negocio: {Estado} en {Metodo} {Ruta}: {Detalle}",
            estado, http.Request.Method, http.Request.Path, dominio.Message);

        http.Response.StatusCode = estado;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = excepcion,
            ProblemDetails = new ProblemDetails { Status = estado, Title = titulo, Detail = dominio.Message }
        });
    }
}
