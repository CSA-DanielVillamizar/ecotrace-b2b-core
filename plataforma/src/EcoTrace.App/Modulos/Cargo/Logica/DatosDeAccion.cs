using System.Text.Json;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// Lo que se guarda en la cola cuando el conductor cambia el estado de una carga. Es el contenido de las dos acciones del
/// módulo (<c>CambiarEstado</c> y <c>ConfirmarEntrega</c>), así que la cola, la pantalla y los manejadores leen siempre la
/// misma forma.
/// </summary>
/// <param name="CargaId">La carga sobre la que se actúa.</param>
/// <param name="Estado">El estado al que se quiere pasar, escrito como lo escribe el servidor (por ejemplo «EnTransito»).</param>
/// <param name="Ubicacion">Dónde ocurre el cambio. El servidor la exige, de 2 a 120 caracteres.</param>
/// <param name="Nota">Una explicación opcional. Es obligatoria cuando el estado es «Con novedad».</param>
public sealed record DatosCambioDeEstado(Guid CargaId, string Estado, string Ubicacion, string? Nota);

/// <summary>Convierte los datos de una acción a texto (JSON) para guardarlos en la cola, y de texto a datos para leerlos.</summary>
public static class PayloadDeCarga
{
    /// <summary>Las reglas de escritura del JSON: nombres en camelCase, igual que el resto del sistema.</summary>
    private static readonly JsonSerializerOptions Opciones = new(JsonSerializerDefaults.Web);

    /// <summary>Convierte los datos de una acción en texto para guardarlos en la cola.</summary>
    public static string Serializar(DatosCambioDeEstado datos) => JsonSerializer.Serialize(datos, Opciones);

    /// <summary>
    /// Lee los datos de una acción guardada. Devuelve null si el texto no es válido o le faltan datos esenciales, en vez de
    /// lanzar un error: una acción con datos ilegibles no debe tumbar la pantalla ni el motor de sincronización.
    /// </summary>
    public static DatosCambioDeEstado? Leer(string payload)
    {
        try
        {
            var datos = JsonSerializer.Deserialize<DatosCambioDeEstado>(payload, Opciones);
            return datos is { CargaId: var id, Estado: { Length: > 0 }, Ubicacion: { Length: > 0 } } && id != Guid.Empty
                ? datos
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
