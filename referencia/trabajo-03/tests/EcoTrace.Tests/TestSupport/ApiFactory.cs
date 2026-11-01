using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace EcoTrace.Tests.TestSupport;

/// <summary>
/// Levanta una API real en memoria con su propia base SQLite temporal. Cada fixture obtiene un
/// archivo distinto, asi las pruebas de un modulo nunca comparten datos con las de otro.
/// </summary>
public sealed class ApiFactory<TMarker> : WebApplicationFactory<TMarker>
    where TMarker : class
{
    private readonly string _archivoDb = Path.Combine(Path.GetTempPath(), $"ecotrace-prueba-{Guid.NewGuid():N}.db");

    /// <summary>
    /// El Identity de mentira de las pruebas de un solo módulo: sirve el JWKS con la clave de prueba. Cuando la prueba
    /// levanta los cuatro servicios (Ecosistema), este cable se reemplaza por el del Identity real.
    /// </summary>
    public JwksFalso Jwks { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(servicios =>
            servicios.AddHttpClient("identity-jwks").ConfigurePrimaryHttpMessageHandler(() => Jwks));
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_archivoDb}");
        builder.UseSetting("Swagger:Habilitado", "false");
        SeguridadDePrueba.Configurar(builder);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        foreach (var sufijo in new[] { string.Empty, "-shm", "-wal" })
        {
            var ruta = _archivoDb + sufijo;
            if (File.Exists(ruta))
            {
                File.Delete(ruta);
            }
        }
    }
}
