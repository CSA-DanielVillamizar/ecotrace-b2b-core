using EcoTrace.App.Modulos.Cargo;

namespace EcoTrace.Mobile.Tests;

/// <summary>La copia de cargas en SQLite de verdad: guarda, reemplaza y sobrevive a cerrar y abrir la app.</summary>
public sealed class CacheDeCargasSqliteTests : IDisposable
{
    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"ecotrace-cargo-cache-{Guid.NewGuid():N}.db");

    private static readonly DateTime Momento = new(2026, 11, 12, 15, 30, 0, DateTimeKind.Utc);

    private static CargaEnCache Carga(string descripcion, string estado = "EnTransito") =>
        new(Guid.NewGuid(), descripcion, "Cali", "Popayán", 4200, estado, Momento);

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

    [Fact]
    public async Task Una_copia_nueva_esta_vacia()
    {
        var cache = new CacheDeCargasSqlite(_ruta);

        Assert.Empty(await cache.ListarAsync());
    }

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

    [Fact]
    public async Task Reemplazar_deja_solo_la_lista_nueva()
    {
        var cache = new CacheDeCargasSqlite(_ruta);
        await cache.ReemplazarAsync([Carga("Vieja uno"), Carga("Vieja dos")]);
        var nueva = Carga("Nueva");

        await cache.ReemplazarAsync([nueva]);

        Assert.Equal(nueva, Assert.Single(await cache.ListarAsync()));
    }

    [Fact]
    public async Task Una_lista_vacia_del_servidor_vacia_la_copia()
    {
        var cache = new CacheDeCargasSqlite(_ruta);
        await cache.ReemplazarAsync([Carga("Una")]);

        await cache.ReemplazarAsync([]);

        Assert.Empty(await cache.ListarAsync());
    }

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
