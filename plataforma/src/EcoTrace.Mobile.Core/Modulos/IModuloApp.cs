using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Mobile.Core.Modulos;

/// <summary>
/// Lo que cada escuadrón implementa para engancharse a la app: un nombre, un título para el menú y los
/// manejadores de las acciones que encola. La pantalla del módulo vive en <c>EcoTrace.App/Modulos/&lt;Nombre&gt;</c>.
/// </summary>
public interface IModuloApp
{
    /// <summary>Identificador estable del módulo: "Identity", "Fleet", "Cargo" o "Billing".</summary>
    string Nombre { get; }

    /// <summary>Texto del menú.</summary>
    string Titulo { get; }

    /// <summary>Un manejador por cada tipo de acción que el módulo encola. Puede estar vacío mientras no encole nada.</summary>
    IReadOnlyList<IManejadorOperacion> Manejadores { get; }
}

/// <summary>Reúne los módulos y valida que el reparto sea coherente antes de que la app arranque.</summary>
public sealed class RegistroDeModulos
{
    private readonly Dictionary<(string Modulo, string Tipo), IManejadorOperacion> _manejadores = new();

    public RegistroDeModulos(IEnumerable<IModuloApp> modulos)
    {
        Modulos = modulos.ToList();

        var nombres = Modulos.Select(m => m.Nombre).ToList();
        if (nombres.Count != nombres.Distinct(StringComparer.Ordinal).Count())
        {
            throw new InvalidOperationException("Dos módulos se llaman igual.");
        }

        foreach (var modulo in Modulos)
        {
            foreach (var manejador in modulo.Manejadores)
            {
                if (manejador.Modulo != modulo.Nombre)
                {
                    throw new InvalidOperationException(
                        $"El manejador '{manejador.Tipo}' dice ser del módulo '{manejador.Modulo}' pero lo registró '{modulo.Nombre}'. " +
                        "Un módulo solo envía sus propias acciones.");
                }

                if (!_manejadores.TryAdd((manejador.Modulo, manejador.Tipo), manejador))
                {
                    throw new InvalidOperationException($"El módulo '{modulo.Nombre}' registró dos veces la acción '{manejador.Tipo}'.");
                }
            }
        }
    }

    public IReadOnlyList<IModuloApp> Modulos { get; }

    public IManejadorOperacion? Manejador(string modulo, string tipo) =>
        _manejadores.GetValueOrDefault((modulo, tipo));
}

/// <summary>
/// Entorno en el que corre la app y las direcciones de los servicios. Sale de un archivo de configuración incrustado
/// (<c>appsettings.json</c>): el build de Debug incrusta el de desarrollo y el de Release el de producción, así que
/// ninguna dirección ni el emisor viven como constantes en el código (ADR 0006). En el emulador de Android,
/// <c>10.0.2.2</c> es el computador donde corre la plataforma.
/// </summary>
public sealed record ConfiguracionServicios(string Entorno, Uri Identity, Uri Fleet, Uri Cargo, Uri Billing)
{
    public static readonly string[] EntornosValidos = ["desarrollo", "produccion"];

    public static ConfiguracionServicios Leer(string json)
    {
        using var documento = System.Text.Json.JsonDocument.Parse(json);
        var raiz = documento.RootElement;

        var entorno = Texto(raiz, "entorno")?.ToLowerInvariant();
        if (entorno is null || !EntornosValidos.Contains(entorno))
        {
            throw new InvalidOperationException("La configuración debe declarar entorno: 'desarrollo' o 'produccion'.");
        }

        if (!raiz.TryGetProperty("servicios", out var servicios))
        {
            throw new InvalidOperationException("La configuración no trae la sección 'servicios'.");
        }

        return new ConfiguracionServicios(
            entorno, Direccion(servicios, "identity"), Direccion(servicios, "fleet"),
            Direccion(servicios, "cargo"), Direccion(servicios, "billing"));
    }

    public bool EsProduccion => Entorno == "produccion";

    private static string? Texto(System.Text.Json.JsonElement elemento, string nombre) =>
        elemento.TryGetProperty(nombre, out var valor) && valor.ValueKind == System.Text.Json.JsonValueKind.String
            ? valor.GetString()
            : null;

    private static Uri Direccion(System.Text.Json.JsonElement servicios, string nombre) =>
        Uri.TryCreate(Texto(servicios, nombre), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? uri
            : throw new InvalidOperationException($"La dirección del servicio '{nombre}' falta o no es una URL http(s) válida.");
}
