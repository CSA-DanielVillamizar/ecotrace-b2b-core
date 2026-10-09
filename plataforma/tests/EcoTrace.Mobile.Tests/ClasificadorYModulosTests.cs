using System.Net;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Conexion;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Mobile.Tests;

/// <summary>La clasificación de conflictos A, B y C del ADR 0005 y el reparto de módulos.</summary>
public sealed class ClasificadorYModulosTests : IDisposable
{
    private readonly BaseTemporal _base = new();

    public void Dispose() => _base.Dispose();

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    public void Un_2xx_es_exito(HttpStatusCode estado) =>
        Assert.Equal(TipoResultado.Exito, ClasificadorDeRespuesta.Clasificar(estado, null).Tipo);

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public void Un_5xx_un_408_y_un_429_son_transitorios(HttpStatusCode estado) =>
        Assert.Equal(TipoResultado.Transitorio, ClasificadorDeRespuesta.Clasificar(estado, null).Tipo);

    [Fact]
    public void Un_401_no_es_un_rechazo_de_la_accion_sino_del_token() =>
        Assert.Equal(TipoResultado.NoAutorizado, ClasificadorDeRespuesta.Clasificar(HttpStatusCode.Unauthorized, null).Tipo);

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "A", TipoConflicto.A)]
    [InlineData(HttpStatusCode.Conflict, "A", TipoConflicto.A)]
    [InlineData(HttpStatusCode.Conflict, "B", TipoConflicto.B)]
    [InlineData(HttpStatusCode.Conflict, "C", TipoConflicto.C)]
    [InlineData(HttpStatusCode.Conflict, "c", TipoConflicto.C)]
    public void El_tipo_de_conflicto_que_declara_el_servidor_manda(HttpStatusCode estado, string tipo, TipoConflicto esperado)
    {
        var cuerpo = $"{{\"title\":\"x\",\"detail\":\"Motivo\",\"tipoConflicto\":\"{tipo}\"}}";

        var resultado = ClasificadorDeRespuesta.Clasificar(estado, cuerpo);

        Assert.Equal(TipoResultado.Rechazo, resultado.Tipo);
        Assert.Equal(esperado, resultado.Conflicto);
        Assert.Equal("Motivo", resultado.Motivo);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, TipoConflicto.A)]
    [InlineData(HttpStatusCode.Conflict, TipoConflicto.B)]
    [InlineData(HttpStatusCode.NotFound, TipoConflicto.B)]
    [InlineData(HttpStatusCode.Gone, TipoConflicto.B)]
    public void Si_el_servidor_no_dice_el_tipo_se_usa_un_valor_conservador_y_nunca_se_infiere_C(HttpStatusCode estado, TipoConflicto esperado)
    {
        var resultado = ClasificadorDeRespuesta.Clasificar(estado, """{"title":"Algo pasó"}""");

        Assert.Equal(esperado, resultado.Conflicto);
        Assert.Equal("Algo pasó", resultado.Motivo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("esto no es json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"tipoConflicto":"Z"}""")]
    [InlineData("""{"tipoConflicto":7}""")]
    public void Un_cuerpo_ausente_o_ilegible_no_rompe_la_clasificacion(string? cuerpo)
    {
        var resultado = ClasificadorDeRespuesta.Clasificar(HttpStatusCode.Conflict, cuerpo);

        Assert.Equal(TipoResultado.Rechazo, resultado.Tipo);
        Assert.Equal(TipoConflicto.B, resultado.Conflicto);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Motivo));
    }

    [Fact]
    public void Un_403_es_un_rechazo_por_permisos_y_conserva_el_motivo()
    {
        var resultado = ClasificadorDeRespuesta.Clasificar(HttpStatusCode.Forbidden, null);

        Assert.Equal(TipoResultado.Rechazo, resultado.Tipo);
        Assert.Contains("403", resultado.Motivo);
    }

    [Fact]
    public void Un_modulo_no_puede_registrar_la_accion_de_otro()
    {
        var intruso = new ModuloConManejadorAjeno();

        var ex = Assert.Throws<InvalidOperationException>(() => new RegistroDeModulos([intruso]));

        Assert.Contains("solo envía sus propias acciones", ex.Message);
    }

    [Fact]
    public void No_se_aceptan_dos_modulos_con_el_mismo_nombre_ni_la_misma_accion_repetida()
    {
        Assert.Throws<InvalidOperationException>(
            () => new RegistroDeModulos([new ModuloDePrueba("Cargo"), new ModuloDePrueba("Cargo")]));
        Assert.Throws<InvalidOperationException>(
            () => new RegistroDeModulos([new ModuloDePrueba("Cargo", "ConfirmarEntrega", "ConfirmarEntrega")]));
    }

    [Fact]
    public void El_registro_encuentra_el_manejador_por_modulo_y_tipo()
    {
        var registro = new RegistroDeModulos([new ModuloDePrueba("Cargo", "ConfirmarEntrega"), new ModuloDePrueba("Billing", "RegistrarEvidencia")]);

        Assert.NotNull(registro.Manejador("Cargo", "ConfirmarEntrega"));
        Assert.Null(registro.Manejador("Cargo", "RegistrarEvidencia"));
        Assert.Equal(2, registro.Modulos.Count);
    }

    [Fact]
    public void Un_modulo_puede_estar_vacio_mientras_su_escuadron_no_encole_nada() =>
        Assert.Empty(new RegistroDeModulos([new ModuloDePrueba("Identity")]).Modulos.Single().Manejadores);

    [Theory]
    [InlineData(10, 30, "10:30 a. m.")]
    [InlineData(0, 5, "12:05 a. m.")]
    [InlineData(12, 0, "12:00 p. m.")]
    [InlineData(15, 45, "3:45 p. m.")]
    [InlineData(23, 59, "11:59 p. m.")]
    public void La_hora_se_escribe_como_en_el_texto_del_contrato(int hora, int minuto, string esperado) =>
        Assert.Equal(esperado, BannerSinConexion.FormatearHora(new DateTime(2026, 10, 12, hora, minuto, 0)));

    [Fact]
    public void El_texto_del_banner_es_el_de_la_especificacion()
    {
        var ahora = new DateTime(2026, 10, 12, 18, 0, 0, DateTimeKind.Utc);
        var ultima = new DateTime(2026, 10, 12, 10, 30, 0, DateTimeKind.Utc);

        Assert.Equal(
            "Sin conexión. Mostrando datos guardados. Última sincronización: 10:30 a. m.",
            BannerSinConexion.ConstruirTexto(ultima, ahora, TimeZoneInfo.Utc));
    }

    [Fact]
    public void El_banner_indica_el_dia_cuando_la_ultima_sincronizacion_no_fue_hoy()
    {
        var ahora = new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc);
        var ultima = new DateTime(2026, 10, 12, 16, 15, 0, DateTimeKind.Utc);

        Assert.Equal(
            "Sin conexión. Mostrando datos guardados. Última sincronización: 12/10 4:15 p. m.",
            BannerSinConexion.ConstruirTexto(ultima, ahora, TimeZoneInfo.Utc));
    }

    [Fact]
    public void El_banner_lo_dice_si_nunca_hubo_una_sincronizacion() =>
        Assert.EndsWith("Aún no hay una sincronización.", BannerSinConexion.ConstruirTexto(null, DateTime.UtcNow, TimeZoneInfo.Utc));

    [Fact]
    public async Task El_banner_solo_se_ve_sin_senal_y_se_actualiza_con_la_ultima_sincronizacion()
    {
        var conectividad = new ConectividadFalsa();
        var cola = _base.Abrir();
        var banner = new BannerSinConexion(conectividad, cola);
        var cambios = new List<string?>();
        banner.PropertyChanged += (_, e) => cambios.Add(e.PropertyName);

        await banner.ActualizarAsync();
        Assert.False(banner.Visible);

        conectividad.HayConexion = false;
        for (var i = 0; i < 100 && !banner.Visible; i++)
        {
            await Task.Delay(20);
        }

        Assert.True(banner.Visible);
        Assert.StartsWith("Sin conexión. Mostrando datos guardados.", banner.Texto);
        Assert.Contains(nameof(BannerSinConexion.Visible), cambios);
    }

    private sealed class ModuloConManejadorAjeno : IModuloApp
    {
        public string Nombre => "Fleet";

        public string Titulo => "Fleet";

        public IReadOnlyList<IManejadorOperacion> Manejadores { get; } = [new ManejadorDePrueba("Billing", "RegistrarEvidencia")];
    }
}
