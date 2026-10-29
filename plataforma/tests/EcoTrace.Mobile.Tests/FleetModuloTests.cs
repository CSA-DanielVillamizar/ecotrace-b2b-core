using System.Net;
using System.Text;
using EcoTrace.App.Modulos.Fleet;
using EcoTrace.Mobile.Core.Modulos;

namespace EcoTrace.Mobile.Tests;

/// <summary>
/// El módulo Fleet de la app sin emulador: la caché del vehículo y el conductor, y de dónde sale lo que ve el
/// conductor con y sin señal (ADR 0005). Fleet se reemplaza por un servidor falso.
/// </summary>
public sealed class FleetModuloTests : IDisposable
{
    private static readonly Guid ConductorId = Guid.Parse("c0000000-0000-4000-8000-000000000001");
    private static readonly Guid VehiculoId = Guid.Parse("f0000000-0000-4000-8000-000000000001");
    private static readonly Guid CargaId = Guid.Parse("ca000000-0000-4000-8000-000000000001");

    private static readonly ConfiguracionServicios Servicios = new(
        "desarrollo", new Uri("http://identity.prueba"), new Uri("http://fleet.prueba"),
        new Uri("http://cargo.prueba"), new Uri("http://billing.prueba"));

    private readonly BaseTemporal _baseCola = new();
    private readonly BaseTemporal _baseCache = new();
    private readonly ServidorFalso _fleet = new();
    private readonly SesionFalsa _sesion = new();
    private readonly ConectividadFalsa _conectividad = new();
    private readonly RelojDePrueba _reloj = new(new DateTimeOffset(2026, 10, 29, 15, 30, 0, TimeSpan.Zero));
    private readonly HttpClient _http;
    private readonly CacheDeFleetSqlite _cache;
    private readonly ServicioFleet _servicio;

    public FleetModuloTests()
    {
        _http = new HttpClient(_fleet);
        _cache = new CacheDeFleetSqlite(_baseCache.Ruta);
        var cliente = new ClienteFleet(_http, Servicios, _sesion, _reloj);
        _servicio = new ServicioFleet(cliente, _cache, _baseCola.Abrir(_reloj), _conectividad, _reloj);
        _fleet.Responder = (_, _) => Task.FromResult(RespuestaYo(conVehiculo: true));
    }

    public void Dispose()
    {
        _http.Dispose();
        _baseCola.Dispose();
        _baseCache.Dispose();
    }

    // ---- Caché ---------------------------------------------------------------------------------

    [Fact]
    public async Task La_cache_sobrevive_a_cerrar_y_abrir_la_app()
    {
        var asignacion = new AsignacionEnCache(ConductorId, "Carlos Ruiz", "C2-1", "Reservado",
            VehiculoId, "ABC123", 5000, "Reservado", CargaId, DateTime.UtcNow);
        await _cache.GuardarAsync(asignacion);

        var otraVez = await new CacheDeFleetSqlite(_baseCache.Ruta).LeerAsync();

        Assert.Equal(asignacion, otraVez);
    }

    [Fact]
    public async Task Sin_nada_guardado_la_cache_devuelve_null()
    {
        Assert.Null(await _cache.LeerAsync());
    }

    // ---- Con señal -----------------------------------------------------------------------------

    [Fact]
    public async Task Con_senal_pide_su_asignacion_a_Fleet_con_el_token_de_la_sesion()
    {
        var vista = await _servicio.RefrescarAsync();

        var solicitud = Assert.Single(_fleet.Recibidas);
        Assert.Equal(HttpMethod.Get, solicitud.Metodo);
        Assert.Equal("http://fleet.prueba/api/conductores/yo", solicitud.Direccion.ToString());
        Assert.Equal("Bearer token-vigente", solicitud.Authorization);
        Assert.False(vista.SonDatosGuardados);
        Assert.Equal("ABC123", vista.Asignacion!.Placa);
        Assert.Equal(CargaId, vista.Asignacion.CargaId);
    }

    [Fact]
    public async Task Con_senal_guarda_la_asignacion_y_registra_la_ultima_sincronizacion()
    {
        await _servicio.RefrescarAsync();

        var guardada = await _cache.LeerAsync();
        Assert.Equal(VehiculoId, guardada!.VehiculoId);
        Assert.Equal(_reloj.GetUtcNow().UtcDateTime, guardada.GuardadaEnUtc);
        Assert.Equal(_reloj.GetUtcNow().UtcDateTime, await _baseCola.Abrir().UltimaSincronizacionAsync());
    }

    [Fact]
    public async Task Sin_carga_asignada_muestra_al_conductor_sin_vehiculo()
    {
        _fleet.Responder = (_, _) => Task.FromResult(RespuestaYo(conVehiculo: false));

        var vista = await _servicio.RefrescarAsync();

        Assert.Equal("Carlos Ruiz", vista.Asignacion!.NombreConductor);
        Assert.False(vista.Asignacion.TieneVehiculo);
        Assert.Null(vista.Asignacion.CargaId);
    }

    // ---- Sin señal: la pantalla nunca queda en blanco -----------------------------------------

    [Fact]
    public async Task Sin_senal_muestra_lo_guardado_con_un_aviso_y_no_llama_a_Fleet()
    {
        await _servicio.RefrescarAsync();
        _conectividad.HayConexion = false;

        var vista = await _servicio.RefrescarAsync();

        Assert.Single(_fleet.Recibidas);
        Assert.True(vista.SonDatosGuardados);
        Assert.Equal("ABC123", vista.Asignacion!.Placa);
        Assert.Contains("Sin conexión", vista.Aviso);
    }

    [Fact]
    public async Task Si_Fleet_no_responde_muestra_lo_guardado_con_el_motivo()
    {
        await _servicio.RefrescarAsync();
        _fleet.Responder = (_, _) => throw new HttpRequestException("Se cayó la red.");

        var vista = await _servicio.RefrescarAsync();

        Assert.True(vista.SonDatosGuardados);
        Assert.Equal("ABC123", vista.Asignacion!.Placa);
        Assert.Contains("No hay conexión con Fleet", vista.Aviso);
    }

    [Fact]
    public async Task Sin_sesion_no_llama_a_Fleet_y_pide_iniciar_sesion()
    {
        _sesion.Token = null;

        var vista = await _servicio.RefrescarAsync();

        Assert.Empty(_fleet.Recibidas);
        Assert.Contains("Inicia sesión", vista.Aviso);
    }

    [Fact]
    public async Task Un_401_le_avisa_a_la_sesion_de_Identity()
    {
        _fleet.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var vista = await _servicio.RefrescarAsync();

        Assert.Equal(1, _sesion.TokenRechazado);
        Assert.Contains("no aceptó tu sesión", vista.Aviso);
    }

    [Fact]
    public async Task Un_usuario_que_no_es_conductor_recibe_un_mensaje_claro()
    {
        _fleet.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

        var vista = await _servicio.RefrescarAsync();

        Assert.Null(vista.Asignacion);
        Assert.Contains("no está registrado como conductor", vista.Aviso);
    }

    /// <summary>Lo mismo que responde Fleet en GET /api/conductores/yo.</summary>
    private static HttpResponseMessage RespuestaYo(bool conVehiculo)
    {
        var vehiculo = conVehiculo
            ? $$"""{ "vehiculoId": "{{VehiculoId}}", "placa": "ABC123", "capacidadKg": 5000, "estado": "Reservado" }"""
            : "null";
        var carga = conVehiculo ? $"\"{CargaId}\"" : "null";
        var json = $$"""
            {
              "conductor": { "conductorId": "{{ConductorId}}", "nombre": "Carlos Ruiz", "licencia": "C2-1", "estado": "Reservado" },
              "vehiculo": {{vehiculo}},
              "cargaId": {{carga}}
            }
            """;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
