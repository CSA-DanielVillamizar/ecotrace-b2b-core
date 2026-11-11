using System.Net;
using System.Text.Json;
using EcoTrace.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;

namespace EcoTrace.Tests;

/// <summary>
/// Que desarrollo y producción no se mezclen (ADR 0006). Hay dos entornos: desarrollo y una «producción simulada» con otra
/// configuración y otro emisor de tokens. Estas pruebas miran las dos capas de la verificación de release en la parte de
/// Cargo &amp; Tracking: la configuración (Capa 1) y el comportamiento real del servicio (Capa 2).
/// </summary>
public sealed class EntornoCargoTests : IAsyncLifetime
{
    /// <summary>El emisor de tokens de desarrollo.</summary>
    private const string EmisorDeDesarrollo = "https://identity.ecotrace.local/desarrollo";
    /// <summary>El emisor de tokens de producción.</summary>
    private const string EmisorDeProduccion = "https://identity.ecotrace.local/produccion";

    /// <summary>El servicio de Cargo levantado en memoria.</summary>
    private readonly ApiFactory<CargoTrackingApiMarker> _fabrica = new();

    /// <summary>No hay nada que preparar antes de cada prueba.</summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Apaga el servicio al terminar la prueba.</summary>
    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    /// <summary>Busca la carpeta del proyecto de Cargo subiendo desde donde corren las pruebas hasta encontrar la solución.</summary>
    private static string CarpetaDeLaApi()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "EcoTrace.sln")))
        {
            carpeta = carpeta.Parent;
        }

        Assert.NotNull(carpeta);
        return Path.Combine(carpeta.FullName, "src", "EcoTrace.CargoTracking", "Api");
    }

    /// <summary>Lee el emisor de tokens que dice un archivo de configuración.</summary>
    private static string EmisorDe(string archivo)
    {
        using var documento = JsonDocument.Parse(File.ReadAllText(Path.Combine(CarpetaDeLaApi(), archivo)));
        return documento.RootElement.GetProperty("Jwt").GetProperty("Emisor").GetString()!;
    }

    // ------------------------------------------------------------------ Capa 2: lo que el servicio dice de sí mismo

    /// <summary>CAPA 2: le pregunta al servicio en qué entorno está, sin token. Responde su nombre, «desarrollo», su versión y el emisor que reconoce.</summary>
    [Fact]
    public async Task El_servicio_dice_en_que_entorno_corre_sin_pedir_token()
    {
        var respuesta = await _fabrica.CreateClient().ConsultarAsync("/api/_meta/entorno");

        Assert.Equal(HttpStatusCode.OK, respuesta.Estado);
        Assert.Equal("CargoTracking", respuesta.Texto("servicio"));
        Assert.Equal("desarrollo", respuesta.Texto("entorno"));
        Assert.False(string.IsNullOrWhiteSpace(respuesta.Texto("version")));
        Assert.Equal(SeguridadDePrueba.Emisor, respuesta.Texto("emisor"));
    }

    /// <summary>CAPA 2: arrancado como producción, el servicio responde «produccion».</summary>
    [Fact]
    public async Task En_produccion_el_servicio_se_anuncia_como_produccion()
    {
        var enProduccion = _fabrica.WithWebHostBuilder(b => b.UseEnvironment("Production"));

        var respuesta = await enProduccion.CreateClient().ConsultarAsync("/api/_meta/entorno");

        Assert.Equal(HttpStatusCode.OK, respuesta.Estado);
        Assert.Equal("produccion", respuesta.Texto("entorno"));
    }

    /// <summary>CAPA 2: con el emisor de producción, Cargo acepta un token de producción y rechaza con 401 uno de desarrollo.</summary>
    [Fact]
    public async Task Cargo_en_produccion_acepta_el_emisor_de_produccion_y_rechaza_el_de_desarrollo()
    {
        var enProduccion = _fabrica.WithWebHostBuilder(b => b.UseSetting("Jwt:Emisor", EmisorDeProduccion));
        var organizacion = Guid.NewGuid();
        var deProduccion = SeguridadDePrueba.Emitir(Rol.Supervisor, organizacion, emisor: EmisorDeProduccion);
        var deDesarrollo = SeguridadDePrueba.Emitir(Rol.Supervisor, organizacion, emisor: EmisorDeDesarrollo);

        var conTokenDeProduccion = await enProduccion.CreateClient().ConToken(deProduccion).GetAsync("/api/cargas");
        var conTokenDeDesarrollo = await enProduccion.CreateClient().ConToken(deDesarrollo).GetAsync("/api/cargas");

        Assert.Equal(HttpStatusCode.OK, conTokenDeProduccion.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, conTokenDeDesarrollo.StatusCode);
    }

    // ------------------------------------------------------------------ Capa 1: la configuración que va en el repositorio

    /// <summary>CAPA 1: en los archivos de configuración, el emisor de producción es distinto al de desarrollo.</summary>
    [Fact]
    public void El_emisor_de_produccion_es_distinto_al_de_desarrollo_en_la_configuracion()
    {
        var desarrollo = EmisorDe("appsettings.json");
        var produccion = EmisorDe("appsettings.Production.json");

        Assert.Equal(EmisorDeDesarrollo, desarrollo);
        Assert.Equal(EmisorDeProduccion, produccion);
        Assert.NotEqual(desarrollo, produccion);
    }

    /// <summary>CAPA 1: los archivos de configuración que van al repositorio (el común y el de producción) no contienen contraseñas ni claves.</summary>
    [Fact]
    public void La_configuracion_comun_y_la_de_produccion_no_llevan_secretos()
    {
        // El secreto de desarrollo vive solo en appsettings.Development.json. En producción se entrega por variable de entorno.
        foreach (var archivo in new[] { "appsettings.json", "appsettings.Production.json" })
        {
            var texto = File.ReadAllText(Path.Combine(CarpetaDeLaApi(), archivo));

            Assert.DoesNotContain("secret", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("password", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("privatekey", texto, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>CAPA 1: en producción, Swagger (la documentación interactiva) queda apagado.</summary>
    [Fact]
    public void En_produccion_no_se_expone_la_documentacion_interactiva()
    {
        using var documento = JsonDocument.Parse(File.ReadAllText(Path.Combine(CarpetaDeLaApi(), "appsettings.Production.json")));

        Assert.False(documento.RootElement.GetProperty("Swagger").GetProperty("Habilitado").GetBoolean());
    }
}
