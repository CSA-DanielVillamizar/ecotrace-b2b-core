using System.Net;
using System.Text;
using System.Text.Json;
using EcoTrace.App.Modulos.Fleet;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Mobile.Tests;

/// <summary>
/// El módulo Fleet de la app sin emulador: la caché del vehículo y el conductor, de dónde sale lo que ve el
/// conductor con y sin señal, y la novedad que se encola y se sincroniza (ADR 0005). Fleet se reemplaza por un
/// servidor falso.
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
    private readonly ColaLocalSqlite _cola;
    private readonly ServicioFleet _servicio;
    private readonly MotorSincronizacion _motor;

    public FleetModuloTests()
    {
        _http = new HttpClient(_fleet);
        _cache = new CacheDeFleetSqlite(_baseCache.Ruta);
        _cola = _baseCola.Abrir(_reloj);
        var cliente = new ClienteFleet(_http, Servicios, _sesion, _reloj);
        _servicio = new ServicioFleet(cliente, _cache, _cola, _conectividad, _reloj);
        _fleet.Responder = (_, _) => Task.FromResult(RespuestaYo(conVehiculo: true));

        // El mismo motor de la app, con el manejador de Fleet registrado como lo hace ModuloFleet.
        var registro = new RegistroDeModulos([new ModuloFleetDePrueba(new ReportarNovedadHandler(Servicios))]);
        _motor = new MotorSincronizacion(_cola, registro, _sesion, _conectividad, _http);
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

    // ---- Novedad encolada ----------------------------------------------------------------------

    [Fact]
    public async Task El_manejador_arma_el_POST_de_novedades_con_la_carga_y_la_hora_del_telefono()
    {
        var ocurrida = new DateTimeOffset(2026, 10, 29, 10, 0, 0, TimeSpan.Zero);
        var datos = new DatosNovedad(VehiculoId, CargaId, "Llanta", "Llanta pinchada", ocurrida);
        var operacion = new OperacionPendiente
        {
            OperationId = Guid.NewGuid(), Modulo = "Fleet", Tipo = ReportarNovedadHandler.TipoAccion,
            Payload = JsonSerializer.Serialize(datos), Estado = EstadoOperacion.PendienteSync, CreadaEnUtc = DateTime.UtcNow
        };

        using var solicitud = new ReportarNovedadHandler(Servicios).Construir(operacion);
        using var cuerpo = JsonDocument.Parse(await solicitud.Content!.ReadAsStringAsync());

        Assert.Equal(HttpMethod.Post, solicitud.Method);
        Assert.Equal($"http://fleet.prueba/api/vehiculos/{VehiculoId}/novedades", solicitud.RequestUri!.ToString());
        Assert.Equal("Llanta", cuerpo.RootElement.GetProperty("tipo").GetString());
        Assert.Equal(CargaId, cuerpo.RootElement.GetProperty("cargaId").GetGuid());
        Assert.Equal(ocurrida, cuerpo.RootElement.GetProperty("ocurridaEn").GetDateTimeOffset());
    }

    [Fact]
    public async Task Sin_senal_la_novedad_queda_en_cola_con_su_OperationId_y_sobrevive_a_cerrar_la_app()
    {
        await _servicio.RefrescarAsync();
        _conectividad.HayConexion = false;

        var reporte = await _servicio.ReportarNovedadAsync("Mecanica", "  Ruido en el motor  ");

        // Otra instancia de la cola sobre el mismo archivo: es lo que ve la app al volver a abrirse.
        var enCola = Assert.Single(await _baseCola.Abrir().ListarAsync(modulo: "Fleet"));
        var datos = JsonSerializer.Deserialize<DatosNovedad>(enCola.Payload)!;
        Assert.True(reporte.Encolada);
        Assert.Contains("sin conexión", reporte.Mensaje);
        Assert.Equal(EstadoOperacion.PendienteSync, enCola.Estado);
        Assert.NotEqual(Guid.Empty, enCola.OperationId);
        Assert.Equal(ReportarNovedadHandler.TipoAccion, enCola.Tipo);
        Assert.Equal(VehiculoId, datos.VehiculoId);
        Assert.Equal(CargaId, datos.CargaId);
        Assert.Equal("Ruido en el motor", datos.Descripcion);
        Assert.Equal(_reloj.GetUtcNow(), datos.OcurridaEn);
    }

    [Fact]
    public async Task Sin_vehiculo_en_cache_no_encola_nada_y_explica_por_que()
    {
        var reporte = await _servicio.ReportarNovedadAsync("Mecanica", "Ruido en el motor");

        Assert.False(reporte.Encolada);
        Assert.Contains("No tienes un vehículo", reporte.Mensaje);
        Assert.Empty(await _cola.ListarAsync());
    }

    [Fact]
    public async Task Al_volver_la_senal_se_envia_con_el_OperationId_como_Idempotency_Key_y_queda_Sincronizada()
    {
        await _servicio.RefrescarAsync();
        _conectividad.HayConexion = false;
        await _servicio.ReportarNovedadAsync("Llanta", "Llanta pinchada");

        var sinSenal = await _motor.SincronizarAsync();
        _conectividad.HayConexion = true;
        _fleet.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
        var conSenal = await _motor.SincronizarAsync();

        var novedad = Assert.Single(await _cola.ListarAsync(modulo: "Fleet"));
        var envio = _fleet.Recibidas.Last();
        Assert.Equal(ParadaSincronizacion.SinConexion, sinSenal.Parada);
        Assert.Equal(1, conSenal.Sincronizadas);
        Assert.Equal(EstadoOperacion.Sincronizado, novedad.Estado);
        Assert.Equal($"http://fleet.prueba/api/vehiculos/{VehiculoId}/novedades", envio.Direccion.ToString());
        Assert.Equal(novedad.OperationId.ToString("D"), envio.IdempotencyKey);
    }

    [Fact]
    public async Task Un_409_con_conflicto_B_deja_la_novedad_Rechazada_con_su_motivo_y_no_se_pierde()
    {
        await _servicio.RefrescarAsync();
        await _servicio.ReportarNovedadAsync("Mecanica", "Falla en los frenos");
        _fleet.Responder = (_, _) => Task.FromResult(ServidorFalso.Problema(HttpStatusCode.Conflict, "B",
            "El vehículo fue reservado para otra carga mientras no tenías señal. La novedad no se registró."));

        var resultado = await _motor.SincronizarAsync();

        var novedad = Assert.Single(await _cola.ListarAsync(modulo: "Fleet"));
        Assert.Equal(1, resultado.Rechazadas);
        Assert.Equal(EstadoOperacion.Rechazado, novedad.Estado);
        Assert.Equal(TipoConflicto.B, novedad.TipoConflicto);
        Assert.Contains("reservado para otra carga", novedad.MotivoRechazo);
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

    /// <summary>Lo mismo que declara ModuloFleet de la app (que no se compila aquí porque usa MAUI).</summary>
    private sealed class ModuloFleetDePrueba(IManejadorOperacion manejador) : IModuloApp
    {
        public string Nombre => ServicioFleet.Modulo;

        public string Titulo => "Vehículo";

        public IReadOnlyList<IManejadorOperacion> Manejadores { get; } = [manejador];
    }
}
