using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;

namespace EcoTrace.CargoTracking.Api.Extensions;

/// <summary>
/// Lo único que se anota de cada solicitud (esquema mínimo de registro seguro, ADR 0006). Son cinco datos y ninguno es
/// sensible: nunca el token, la contraseña, lo que escribió el usuario ni la clave de idempotencia.
/// </summary>
/// <param name="CorrelationId">El hilo que une todas las llamadas de una misma operación entre servicios.</param>
/// <param name="OperationName">Qué acción se pidió, por ejemplo «Cargas.RegistrarSeguimiento». No es la dirección completa.</param>
/// <param name="HttpStatus">Con qué código se respondió (200, 201, 403, 409...), ya con los errores convertidos en respuesta.</param>
/// <param name="DurationMs">Cuánto tardó, en milisegundos.</param>
/// <param name="EntityId">El identificador de la carga o del mensaje, si la ruta lo trae. Es un número, nunca un dato personal.</param>
public sealed record EntradaDeRegistro(string CorrelationId, string OperationName, int HttpStatus, double DurationMs, string? EntityId);

/// <summary>
/// Dónde se anota cada solicitud. Normalmente es el registro del servidor; las pruebas lo cambian por uno que guarda las
/// entradas para poder revisarlas.
/// </summary>
public interface IRegistroDeSolicitudes
{
    /// <summary>Anota una solicitud ya terminada.</summary>
    void Registrar(EntradaDeRegistro entrada);
}

/// <summary>El registro de siempre: escribe una línea por solicitud en el registro del servidor.</summary>
internal sealed class RegistroDeSolicitudesEnLog(ILogger<RegistroDeSolicitudesEnLog> registro) : IRegistroDeSolicitudes
{
    /// <summary>Escribe una línea como: «Cargas.Obtener -> 200 en 12 ms (entidad 5f3a...) [correlación]».</summary>
    public void Registrar(EntradaDeRegistro entrada) =>
        registro.LogInformation(
            "{OperationName} -> {HttpStatus} en {DurationMs:0} ms (entidad {EntityId}) [{CorrelationId}]",
            entrada.OperationName, entrada.HttpStatus, entrada.DurationMs, entrada.EntityId ?? "-", entrada.CorrelationId);
}

/// <summary>Conecta el registro seguro al servicio: lo registra y agrega el paso que anota cada solicitud.</summary>
public static class RegistroSeguro
{
    /// <summary>Registra en el servicio el registro de solicitudes que escribe en el log.</summary>
    public static WebApplicationBuilder AddRegistroSeguro(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IRegistroDeSolicitudes, RegistroDeSolicitudesEnLog>();
        return builder;
    }

    /// <summary>
    /// Agrega el paso que anota una línea por solicitud. Va <b>antes</b> de los demás pasos a propósito: así envuelve todo,
    /// incluido el paso que convierte un error en respuesta, y anota el código final (409, 403...) y no uno provisional.
    /// <para>
    /// El nombre de la operación y el identificador se toman <b>al empezar</b>, no al terminar. El paso que convierte un error
    /// del dominio en respuesta borra la ruta de la solicitud; si se leyeran al final, justo los rechazos (409, 404, 400)
    /// quedarían anotados como «SinRuta».
    /// </para>
    /// </summary>
    public static WebApplication UseRegistroSeguro(this WebApplication app)
    {
        var registro = app.Services.GetRequiredService<IRegistroDeSolicitudes>();

        app.Use(async (http, siguiente) =>
        {
            // Antes de seguir: después, el manejo de errores puede borrar la ruta y estos dos datos se perderían.
            var operacion = NombreDeLaOperacion(http);
            var entidad = Entidad(http);

            var inicio = Stopwatch.GetTimestamp();
            try
            {
                await siguiente();
            }
            finally
            {
                registro.Registrar(new EntradaDeRegistro(
                    http.CorrelationId(),
                    operacion,
                    http.Response.StatusCode,
                    Stopwatch.GetElapsedTime(inicio).TotalMilliseconds,
                    entidad));
            }
        });

        return app;
    }

    /// <summary>Saca el nombre de la acción (controlador y método), nunca la dirección completa, que puede traer datos.</summary>
    private static string NombreDeLaOperacion(HttpContext http)
    {
        var endpoint = http.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>() is { } accion)
        {
            return $"{accion.ControllerName}.{accion.ActionName}";
        }

        return endpoint?.DisplayName is { Length: > 0 } nombre ? nombre : "SinRuta";
    }

    /// <summary>Saca el identificador de la carga o del mensaje desde la ruta, si lo trae. Si no, devuelve null.</summary>
    private static string? Entidad(HttpContext http) =>
        (http.GetRouteValue("id") ?? http.GetRouteValue("eventoId"))?.ToString();
}
