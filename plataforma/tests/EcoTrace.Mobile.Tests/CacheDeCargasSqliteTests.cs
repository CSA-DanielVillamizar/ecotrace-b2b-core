using EcoTrace.App.Modulos.Cargo;

namespace EcoTrace.Mobile.Tests;

/// <summary>Pruebas de la copia de cargas en SQLite, con un archivo temporal de verdad.</summary>
public sealed class CacheDeCargasSqliteTests : IDisposable
{
    /// <summary>La ruta del archivo temporal de esta prueba.</summary>
    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"ecotrace-cargo-cache-{Guid.NewGuid():N}.db");

    /// <summary>Una fecha fija en UTC para las cargas de prueba.</summary>
    private static readonly DateTime Momento = new(2026, 11, 12, 15, 30, 0, DateTimeKind.Utc);

    /// <summary>Crea una carga de ejemplo para guardar.</summary>
    private static CargaEnCache Carga(string descripcion, string estado = "EnTransito") =>
        new(Guid.NewGuid(), descripcion, "Cali", "Popayán", 4200, estado, Momento);

    /// <summary>Borra el archivo temporal al terminar la prueba.</summary>
    public void Dispose()
    {
        foreach (var archivo in new[] { _ruta, _ruta + "-wal", _ruta + "-shm" })
        {
            try
            {
                File.Delete(archivo);
            }
            catch (IOException)
            {
                // Si el sistema todavía lo tiene abierto, lo limpia el directorio temporal.
            }
        }
    }

    /// <summary>Una copia recién creada no tiene ninguna carga.</summary>
    [Fact]
    public async Task Una_copia_nueva_esta_vacia()
    {
        var cache = new CacheDeCargasSqlite(_ruta);

        Assert.Empty(await cache.ListarAsync());
    }

    /// <summary>Guarda cargas con tildes y comprueba que se lean igual, con la hora en UTC.</summary>
    [Fact]
    public async Task Guarda_las_cargas_con_sus_acentos_y_la_hora_en_UTC()
    {
        var cache = new CacheDeCargasSqlite(_ruta);
        var carga = Carga("Residuos no peligrosos");

        await cache.ReemplazarAsync([carga]);
        var leida = Assert.Single(await cache.ListarAsync());

        Assert.Equal(carga, leida);
        Assert.Equal("Popayán", leida.Destino);
        Assert.Equal(DateTimeKind.Utc, leida.ActualizadaEnUtc.Kind);
    }

    /// <summary>Reemplazar borra lo anterior: solo queda la lista nueva.</summary>
    [Fact]
    public async Task Reemplazar_deja_solo_la_lista_nueva()
    {
        var cache = new CacheDeCargasSqlite(_ruta);
        await cache.ReemplazarAsync([Carga("Vieja uno"), Carga("Vieja dos")]);
        var nueva = Carga("Nueva");

        await cache.ReemplazarAsync([nueva]);

        Assert.Equal(nueva, Assert.Single(await cache.ListarAsync()));
    }

    /// <summary>Si el servidor devuelve una lista vacía, la copia también queda vacía.</summary>
    [Fact]
    public async Task Una_lista_vacia_del_servidor_vacia_la_copia()
    {
        var cache = new CacheDeCargasSqlite(_ruta);
        await cache.ReemplazarAsync([Carga("Una")]);

        await cache.ReemplazarAsync([]);

        Assert.Empty(await cache.ListarAsync());
    }

    /// <summary>Abre una copia nueva sobre el mismo archivo (como al cerrar y abrir la app) y comprueba que las cargas siguen ahí.</summary>
    [Fact]
    public async Task La_copia_sobrevive_a_cerrar_y_abrir_la_app()
    {
        var carga = Carga("Persistente");
        await new CacheDeCargasSqlite(_ruta).ReemplazarAsync([carga]);

        // Una instancia nueva sobre el mismo archivo es lo que pasa al cerrar y volver a abrir la app.
        var despues = await new CacheDeCargasSqlite(_ruta).ListarAsync();

        Assert.Equal(carga, Assert.Single(despues));
    }
}
