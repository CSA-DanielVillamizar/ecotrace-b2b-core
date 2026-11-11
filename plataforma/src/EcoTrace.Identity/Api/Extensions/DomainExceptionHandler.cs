using EcoTrace.Identity.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.Identity.Api.Extensions;

/// <summary>
/// Traduce las DomainException a respuestas ProblemDetails (RFC 9457) y decide con que nivel se
/// registra cada excepcion: una regla de negocio rechazada es informacion, un fallo inesperado
/// es un error. El middleware de ASP.NET 8 registra ambos como error, por eso su categoria se
/// silencia en ApiDefaults y este manejador asume el registro.
/// <para>
/// Trabajo 3 (ADR 0005): un 400 o un 409 llevan <c>tipoConflicto</c>, que es lo que la app lee para decidir
/// qué hacer con la acción de su cola. 400 es A (el dato se puede corregir) y 409 es B (el servidor decidió).
/// C (edición concurrente) no se infiere: Identity no tiene hoy un caso así.
/// </para>
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
            DomainErrorKind.Forbidden => (StatusCodes.Status403Forbidden, "Sin permiso"),
            _ => (StatusCodes.Status500InternalServerError, "Error inesperado")
        };

        registro.LogInformation(
            "Regla de negocio: {Estado} en {Metodo} {Ruta}: {Detalle}",
            estado, http.Request.Method, http.Request.Path, dominio.Message);

        var problema = new ProblemDetails { Status = estado, Title = titulo, Detail = dominio.Message };
        var tipoConflicto = dominio.Kind switch
        {
            DomainErrorKind.Validation => "A",
            DomainErrorKind.Conflict => "B",
            _ => null
        };
        if (tipoConflicto is not null)
        {
            problema.Extensions["tipoConflicto"] = tipoConflicto;
        }

        http.Response.StatusCode = estado;
        return await problemas.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = excepcion,
            ProblemDetails = problema
        });
    }
}
