using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Mobile.Tests;

/// <summary>Los textos de la app y la lectura de la configuración de entorno (ADR 0006).</summary>
public sealed class PresentacionYConfiguracionTests
{
    [Theory]
    [InlineData("ConfirmarEntrega", "Confirmar entrega")]
    [InlineData("CambiarEstado", "Cambiar estado")]
    [InlineData("ReportarNovedadDelVehiculo", "Reportar novedad del vehiculo")]
    [InlineData("Entregar", "Entregar")]
    [InlineData("", "")]
    public void El_nombre_de_la_accion_se_muestra_en_palabras(string tipo, string esperado) =>
        Assert.Equal(esperado, Presentacion.Accion(tipo));

    [Fact]
    public void Cada_estado_y_cada_conflicto_tienen_un_texto_para_el_conductor()
    {
        foreach (var estado in Enum.GetValues<EstadoOperacion>())
        {
            Assert.DoesNotContain(estado.ToString(), Presentacion.Estado(estado), StringComparison.Ordinal);
        }

        foreach (var tipo in Enum.GetValues<TipoConflicto>())
        {
            Assert.StartsWith($"Conflicto {tipo}", Presentacion.Conflicto(tipo));
        }

        Assert.Equal(string.Empty, Presentacion.Conflicto(null));
    }

    [Theory]
    [InlineData(3, 0, 0, ParadaSincronizacion.Ninguna, "3 acciones enviadas.")]
    [InlineData(1, 1, 0, ParadaSincronizacion.Ninguna, "1 acción enviada · 1 rechazada por el servidor.")]
    [InlineData(0, 0, 0, ParadaSincronizacion.Ninguna, "No había nada por enviar.")]
    [InlineData(0, 0, 2, ParadaSincronizacion.SinConexion, "Sin conexión. 2 acciones esperan en cola hasta que vuelva la señal.")]
    [InlineData(0, 0, 1, ParadaSincronizacion.SinSesion, "Inicia sesión para enviar. 1 acción espera en cola.")]
    [InlineData(0, 0, 1, ParadaSincronizacion.ErrorTransitorio, "El servidor no respondió. Se reintenta solo. 1 acción espera en cola.")]
    public void El_resultado_de_sincronizar_se_explica_sin_jerga(
        int sincronizadas, int rechazadas, int pendientes, ParadaSincronizacion parada, string esperado) =>
        Assert.Equal(esperado, Presentacion.Resultado(new(sincronizadas, rechazadas, pendientes, parada)));

    [Fact]
    public void La_configuracion_de_desarrollo_se_lee_del_json()
    {
        var configuracion = ConfiguracionServicios.Leer(
            """{ "entorno": "desarrollo", "servicios": { "identity": "http://10.0.2.2:5101", "fleet": "http://10.0.2.2:5102", "cargo": "http://10.0.2.2:5103", "billing": "http://10.0.2.2:5104" } }""");

        Assert.Equal("desarrollo", configuracion.Entorno);
        Assert.False(configuracion.EsProduccion);
        Assert.Equal(new Uri("http://10.0.2.2:5103"), configuracion.Cargo);
    }

    [Fact]
    public void El_entorno_distingue_mayusculas_de_minusculas_sin_confundirse()
    {
        var configuracion = ConfiguracionServicios.Leer(
            """{ "entorno": "Produccion", "servicios": { "identity": "https://a.example", "fleet": "https://b.example", "cargo": "https://c.example", "billing": "https://d.example" } }""");

        Assert.True(configuracion.EsProduccion);
    }

    [Theory]
    [InlineData("""{ "servicios": {} }""")]
    [InlineData("""{ "entorno": "staging", "servicios": {} }""")]
    [InlineData("""{ "entorno": "desarrollo" }""")]
    [InlineData("""{ "entorno": "desarrollo", "servicios": { "identity": "no-es-url", "fleet": "http://x", "cargo": "http://x", "billing": "http://x" } }""")]
    [InlineData("""{ "entorno": "desarrollo", "servicios": { "identity": "ftp://x", "fleet": "http://x", "cargo": "http://x", "billing": "http://x" } }""")]
    [InlineData("""{ "entorno": "desarrollo", "servicios": { "identity": "http://x", "fleet": "http://x", "cargo": "http://x" } }""")]
    public void Una_configuracion_incompleta_o_con_un_entorno_inventado_se_rechaza_al_arrancar(string json) =>
        Assert.Throws<InvalidOperationException>(() => ConfiguracionServicios.Leer(json));
}
