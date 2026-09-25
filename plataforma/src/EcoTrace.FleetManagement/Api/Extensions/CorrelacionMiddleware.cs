using System.Text.RegularExpressions;
using Serilog.Context;

namespace EcoTrace.FleetManagement.Api.Extensions;

/// <summary>
/// Lee el encabezado X-Correlation-Id, o genera uno si no viene o no tiene el formato acordado, lo
/// devuelve en la respuesta y lo agrega a los registros. Es la regla común del Trabajo 2: todas las
/// llamadas de un mismo Saga llevan el mismo identificador, y así se puede seguir una sola
/// operación por los cuatro servicios.
/// </summary>
public static partial class CorrelacionMiddleware
{
    public const string Encabezado = "X-Correlation-Id";

    public static WebApplication UseCorrelacion(this WebApplication app)
    {
        app.Use(async (http, siguiente) =>
        {
            var recibido = http.Request.Headers[Encabezado].ToString();
            var correlationId = FormatoValido().IsMatch(recibido)
                ? recibido
                : Guid.NewGuid().ToString("N");

            http.Response.Headers[Encabezado] = correlationId;

            // Se empuja al contexto de Serilog para que todos los registros de esta solicitud lo
            // lleven, incluido el que escribe UseSerilogRequestLogging al terminar.
            using (LogContext.PushProperty("CorrelationId", correlationId))
            {
                await siguiente(http);
            }
        });

        return app;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex FormatoValido();
}
