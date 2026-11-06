using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace EcoTrace.Identity.Api.Extensions;

/// <summary>
/// Un registro por solicitud con el esquema mínimo del ADR 0006:
/// <c>{ CorrelationId, EntityId, HttpStatus, OperationName, DurationMs }</c>. El CorrelationId ya viene en el contexto
/// del registro (ver <c>UseApiDefaults</c>).
/// <para>
/// Nunca registra el cuerpo, la consulta ni los encabezados: ahí van contraseñas, tokens y datos de contacto. El
/// <c>EntityId</c> es siempre un identificador (Guid de la ruta o el que fijó el controlador), nunca un correo.
/// </para>
/// </summary>
internal sealed class RegistroDeSolicitudes(RequestDelegate siguiente, ILogger<RegistroDeSolicitudes> registro)
{
    /// <summary>Clave de <c>HttpContext.Items</c> con la que un controlador indica sobre qué entidad trabajó.</summary>
    public const string ClaveEntidad = "EcoTrace.EntityId";

    private static readonly string[] ParametrosDeEntidad = ["id", "pagoId"];

    public async Task InvokeAsync(HttpContext http)
    {
        var cronometro = Stopwatch.StartNew();
        try
        {
            await siguiente(http);
        }
        finally
        {
            cronometro.Stop();
            registro.LogInformation(
                "{OperationName} -> {HttpStatus} en {DurationMs} ms (entidad {EntityId})",
                Operacion(http), http.Response.StatusCode, cronometro.ElapsedMilliseconds, Entidad(http));
        }
    }

    /// <summary>"Auth.Login", "Perfil.Actualizar"... o la ruta registrada si no es un controlador.</summary>
    private static string Operacion(HttpContext http)
    {
        var punto = http.GetEndpoint();
        if (punto?.Metadata.GetMetadata<ControllerActionDescriptor>() is { } accion)
        {
            return $"{accion.ControllerName}.{accion.ActionName}";
        }

        return punto is RouteEndpoint ruta
            ? $"{http.Request.Method} /{ruta.RoutePattern.RawText?.TrimStart('/')}"
            : $"{http.Request.Method} (sin ruta)";
    }

    private static string Entidad(HttpContext http)
    {
        if (http.Items.TryGetValue(ClaveEntidad, out var fijada) && fijada is not null)
        {
            return fijada.ToString()!;
        }

        foreach (var nombre in ParametrosDeEntidad)
        {
            if (http.Request.RouteValues.TryGetValue(nombre, out var valor) && Guid.TryParse(valor?.ToString(), out var id))
            {
                return id.ToString();
            }
        }

        return "-";
    }
}

public static class RegistroDeSolicitudesExtensions
{
    /// <summary>Indica la entidad de la operación para el registro (un identificador, nunca un dato personal).</summary>
    public static void RegistrarEntidad(this HttpContext http, Guid entidad) =>
        http.Items[RegistroDeSolicitudes.ClaveEntidad] = entidad;

    public static void RegistrarEntidad(this HttpContext http, string identificador) =>
        http.Items[RegistroDeSolicitudes.ClaveEntidad] = identificador;
}
