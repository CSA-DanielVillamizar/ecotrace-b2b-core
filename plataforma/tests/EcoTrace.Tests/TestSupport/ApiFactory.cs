using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

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
    /// El Identity de mentira de las pruebas: sirve la clave pública para comprobar firmas y entrega tokens de servicio.
    /// Solo lo usa un módulo que valida tokens (hoy, Cargo &amp; Tracking); para los demás no cambia nada.
    /// </summary>
    public IdentityFalso IdentityDeMentira { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_archivoDb}");
        builder.UseSetting("Swagger:Habilitado", "false");

        // Identity firma con una clave de la corrida, recibida por configuración como en producción.
        // Los demás servicios ignoran esta clave.
        builder.UseSetting("Jwt:ClavePrivadaPem", Acceso.ClavePrivadaPem);

        // Las pruebas de Cargo & Tracking validan contra un Identity de mentira, con su propio emisor y su propio
        // cliente de servicio. Solo se aplica a Cargo: Identity, Fleet y Billing leen las mismas claves de
        // configuración (Jwt:Emisor, Identity:ClientId) y usan las de desarrollo con el Identity real de la prueba.
        if (typeof(TMarker) == typeof(CargoTrackingApiMarker))
        {
            SeguridadDePrueba.Configurar(builder);
            builder.ConfigureTestServices(servicios =>
            {
                servicios.AddHttpClient(SeguridadDePrueba.ClienteJwks).ConfigurePrimaryHttpMessageHandler(() => IdentityDeMentira);
                servicios.AddHttpClient(SeguridadDePrueba.ClienteToken).ConfigurePrimaryHttpMessageHandler(() => IdentityDeMentira);
            });
        }
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
