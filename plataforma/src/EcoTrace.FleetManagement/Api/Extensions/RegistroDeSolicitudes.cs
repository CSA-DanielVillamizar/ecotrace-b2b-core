using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace EcoTrace.FleetManagement.Api.Extensions;

/// <summary>
/// Escribe una línea por solicitud con los campos que pide el Trabajo 3 (ADR 0006): CorrelationId, EntityId,
/// HttpStatus, OperationName y DurationMs. Solo guarda ids y números. Nunca el token, el cuerpo ni datos de
/// personas como nombres o licencias.
/// </summary>
public sealed class RegistroDeSolicitudes(RequestDelegate siguiente, ILogger<RegistroDeSolicitudes> registro)
{
    public async Task InvokeAsync(HttpContext http)
    {
        var cronometro = Stopwatch.StartNew();
        try
        {
            await siguiente(http);
        }
        finally
        {
            // El CorrelationId ya viene en el contexto del registro (lo pone ApiDefaults antes de este paso).
            registro.LogInformation(
                "Solicitud {OperationName} sobre {EntityId}: {HttpStatus} en {DurationMs} ms",
                Operacion(http), Entidad(http), http.Response.StatusCode, cronometro.ElapsedMilliseconds);
        }
    }

    /// <summary>Qué se hizo, por ejemplo "Novedades.Reportar". Sale de la ruta, no de lo que mandó el cliente.</summary>
    private static string Operacion(HttpContext http)
    {
        var ruta = http.GetEndpoint();
        var accion = ruta?.Metadata.GetMetadata<ControllerActionDescriptor>();
        return accion is not null ? $"{accion.ControllerName}.{accion.ActionName}" : ruta?.DisplayName ?? "SinRuta";
    }

    /// <summary>El vehículo o conductor de la ruta, si hay uno. Es un Guid: no dice nada de la persona.</summary>
    private static string Entidad(HttpContext http)
    {
        if (http.Request.RouteValues.TryGetValue("vehiculoId", out var vehiculoId))
        {
            return $"{vehiculoId}";
        }

        return http.Request.RouteValues.TryGetValue("id", out var id) ? $"{id}" : "-";
    }
}
