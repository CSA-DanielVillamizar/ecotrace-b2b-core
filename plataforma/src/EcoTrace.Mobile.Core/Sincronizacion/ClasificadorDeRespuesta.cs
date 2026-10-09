using System.Net;
using System.Text.Json;
using EcoTrace.Mobile.Core.Cola;

namespace EcoTrace.Mobile.Core.Sincronizacion;

/// <summary>
/// Traduce la respuesta del servidor a lo que la app debe hacer con la acción (ADR 0005). Es una función pura:
/// el contrato de la especificación está en un solo lugar y se prueba sin red.
/// <para>
/// Cuando el servidor rechaza, trae un <c>ProblemDetails</c> con la propiedad <c>tipoConflicto</c> (A, B o C).
/// Si falta, se usa un valor conservador: un 400 es A (el dato se puede corregir) y un 409, 404 o 410 es B
/// (el servidor decidió). Nunca se infiere C: una edición concurrente tiene que declararla el servidor.
/// </para>
/// </summary>
public static class ClasificadorDeRespuesta
{
    public static ResultadoEnvio Clasificar(HttpStatusCode estado, string? cuerpo)
    {
        var codigo = (int)estado;

        if (codigo is >= 200 and < 300)
        {
            return new(TipoResultado.Exito, null, string.Empty);
        }

        if (estado == HttpStatusCode.Unauthorized)
        {
            return new(TipoResultado.NoAutorizado, null, "El servidor no aceptó la sesión (401).");
        }

        if (codigo >= 500 || estado is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
        {
            return new(TipoResultado.Transitorio, null, $"El servidor no pudo atender la solicitud ({codigo}).");
        }

        var (conflicto, detalle) = LeerProblema(cuerpo);
        var motivo = detalle ?? $"El servidor rechazó la acción ({codigo}).";

        if (estado == HttpStatusCode.Forbidden)
        {
            return new(TipoResultado.Rechazo, conflicto, detalle ?? "No tienes permiso para esta acción (403).");
        }

        conflicto ??= estado switch
        {
            HttpStatusCode.BadRequest => TipoConflicto.A,
            HttpStatusCode.Conflict or HttpStatusCode.NotFound or HttpStatusCode.Gone => TipoConflicto.B,
            _ => null
        };

        return new(TipoResultado.Rechazo, conflicto, motivo);
    }

    private static (TipoConflicto? Conflicto, string? Detalle) LeerProblema(string? cuerpo)
    {
        if (string.IsNullOrWhiteSpace(cuerpo))
        {
            return (null, null);
        }

        try
        {
            using var documento = JsonDocument.Parse(cuerpo);
            if (documento.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            TipoConflicto? conflicto = null;
            string? detalle = null;
            foreach (var propiedad in documento.RootElement.EnumerateObject())
            {
                if (propiedad.NameEquals("tipoConflicto") && propiedad.Value.ValueKind == JsonValueKind.String
                    && Enum.TryParse<TipoConflicto>(propiedad.Value.GetString(), ignoreCase: true, out var valor)
                    && Enum.IsDefined(valor))
                {
                    conflicto = valor;
                }
                else if ((propiedad.NameEquals("detail") || (detalle is null && propiedad.NameEquals("title")))
                         && propiedad.Value.ValueKind == JsonValueKind.String)
                {
                    detalle = propiedad.Value.GetString();
                }
            }

            return (conflicto, detalle);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
