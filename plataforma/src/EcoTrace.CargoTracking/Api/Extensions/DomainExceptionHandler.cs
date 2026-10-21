using EcoTrace.CargoTracking.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.CargoTracking.Api.Extensions;

/// <summary>
/// Traduce las DomainException a respuestas ProblemDetails (RFC 9457) y decide con que nivel se
/// registra cada excepcion: una regla de negocio rechazada es informacion, un fallo inesperado
/// es un error. El middleware de ASP.NET 8 registra ambos como error, por eso su categoria se
/// silencia en ApiDefaults y este manejador asume el registro.
/// <para>
/// Trabajo 3 (ADR 0005): cuando rechaza con 400 o 409, la respuesta incluye la propiedad
/// <c>tipoConflicto</c>. Es lo que la app del conductor lee para saber qué hacer con la acción
/// que quedó en su cola. Ver <see cref="TipoDeConflicto"/>.
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
            // Se escribe como texto ("A", "B" o "C"). El nombre va en camelCase porque así lo busca la app.
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
    /// Clasifica un rechazo según la taxonomía del ADR 0005, para que la app sepa cómo tratarlo.
    /// <list type="bullet">
    /// <item><b>A</b> (validación recuperable): el dato enviado no cumple una regla y el conductor puede
    /// corregirlo. Corresponde a los errores de validación (400).</item>
    /// <item><b>B</b> (estado autoritativo): el servidor ya tiene otro estado y manda él, por ejemplo una
    /// carga que ya fue entregada. No se reintenta; la acción queda Rechazada con su motivo. Corresponde a
    /// los conflictos (409).</item>
    /// <item><b>C</b> (edición concurrente): dos personas legítimas cambiaron lo mismo. Cargo no tiene hoy
    /// un caso así, por eso no se declara; si aparece, habrá que pedirlo de forma explícita desde el dominio
    /// y no inferirlo aquí.</item>
    /// </list>
    /// Un 404 y un error inesperado no llevan tipo: el clasificador de la app ya trata el 404 como B.
    /// </summary>
    private static string? TipoDeConflicto(DomainErrorKind tipoDeError) => tipoDeError switch
    {
        DomainErrorKind.Validation => "A",
        DomainErrorKind.Conflict => "B",
        _ => null
    };
}
