using EcoTrace.FleetManagement.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.FleetManagement.Api.Extensions;

/// <summary>
/// Traduce las DomainException a respuestas ProblemDetails (RFC 9457) y decide con que nivel se
/// registra cada excepcion: una regla de negocio rechazada es informacion, un fallo inesperado
/// es un error. El middleware de ASP.NET 8 registra ambos como error, por eso su categoria se
/// silencia en ApiDefaults y este manejador asume el registro.
/// <para>
/// Trabajo 3 (ADR 0005): cuando rechaza con 400 o 409, la respuesta incluye <c>tipoConflicto</c>, que es lo
/// que la app del conductor lee para saber qué hacer con la acción de su cola. Es el mismo criterio de Cargo.
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
            _ => (StatusCodes.Status500InternalServerError, "Error inesperado")
        };

        var tipoConflicto = TipoDeConflicto(dominio.Kind);

        registro.LogInformation(
            "Regla de negocio: {Estado} (conflicto {TipoConflicto}) en {Metodo} {Ruta}: {Detalle}",
            estado, tipoConflicto ?? "-", http.Request.Method, http.Request.Path, dominio.Message);

        var problema = new ProblemDetails { Status = estado, Title = titulo, Detail = dominio.Message };
        if (tipoConflicto is not null)
        {
            // Como texto ("A" o "B") y en camelCase, que es como lo busca la app.
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

    /// <summary>
    /// Taxonomía del ADR 0005. <b>A</b>: el dato no cumple una regla y el conductor lo puede corregir (400).
    /// <b>B</b>: el servidor ya tiene otro estado y manda él, por ejemplo el vehículo reservado para otra carga
    /// (409); la acción queda Rechazada y no se reintenta. <b>C</b> (edición concurrente) no se declara: Fleet
    /// no tiene hoy un caso así y no se debe inferir. Un 404 no lleva tipo; la app ya lo trata como B.
    /// </summary>
    private static string? TipoDeConflicto(DomainErrorKind tipoDeError) => tipoDeError switch
    {
        DomainErrorKind.Validation => "A",
        DomainErrorKind.Conflict => "B",
        _ => null
    };
}
