using EcoTrace.CargoTracking.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EcoTrace.CargoTracking.Api.Extensions;

/// <summary>
/// Convierte los errores de negocio (por ejemplo «la carga ya fue asignada») en una respuesta HTTP con un mensaje claro.
/// <para>
/// En palabras simples: cuando el dominio dice «no», este código decide con qué número responder (400, 404 o 409) y arma
/// el mensaje. También decide cómo se anota en el registro: una regla de negocio rechazada es información, y un fallo
/// inesperado es un error.
/// </para>
/// <para>
/// Trabajo 3 (ADR 0005): cuando rechaza con 400 o 409, la respuesta incluye <c>tipoConflicto</c> (A o B). La app del
/// conductor lo lee para saber qué hacer con la acción que quedó en su cola.
/// </para>
/// </summary>
internal sealed class DomainExceptionHandler(
    IProblemDetailsService problemas, ILogger<DomainExceptionHandler> registro) : IExceptionHandler
{
    /// <summary>
    /// Atiende un error. Si es un error de negocio, escribe la respuesta (400, 404 o 409) y devuelve true. Si es cualquier
    /// otro error (un fallo inesperado), solo lo anota y devuelve false para que ASP.NET responda con un 500.
    /// </summary>
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
    /// Dice de qué tipo es un rechazo, para que la app sepa qué hacer con la acción (ADR 0005):
    /// <list type="bullet">
    /// <item><b>A</b>: el dato no es válido y el conductor lo puede corregir y reintentar. Corresponde a un error 400.</item>
    /// <item><b>B</b>: el servidor ya tiene otro estado y manda él, por ejemplo una carga que ya fue entregada. No se
    /// reintenta: la acción queda Rechazada con su motivo. Corresponde a un error 409.</item>
    /// <item><b>C</b>: dos personas editaron lo mismo a la vez. Cargo no tiene un caso así, por eso no se declara.</item>
    /// </list>
    /// Un 404 y un error inesperado no llevan tipo (devuelve null): la app ya trata el 404 como tipo B.
    /// </summary>
    private static string? TipoDeConflicto(DomainErrorKind tipoDeError) => tipoDeError switch
    {
        DomainErrorKind.Validation => "A",
        DomainErrorKind.Conflict => "B",
        _ => null
    };
}
