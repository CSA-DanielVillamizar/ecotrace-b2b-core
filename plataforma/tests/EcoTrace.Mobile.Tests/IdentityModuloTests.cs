using System.Net;
using System.Text;
using System.Text.Json;
using EcoTrace.App.Modulos.Identity.Logica;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Mobile.Tests;

/// <summary>
/// La lógica del módulo Identity de la app (Trabajo 3, ADR 0005), sin MAUI ni emulador: la sesión que alimenta al
/// motor de sincronización, las acciones encoladas del perfil y lo que la pantalla muestra sin señal.
/// </summary>
public sealed class IdentityModuloTests : IDisposable
{
    private static readonly Uri DireccionIdentity = new("http://identity.prueba");
    private static readonly ConfiguracionServicios Servicios = new(
        "desarrollo", DireccionIdentity, new("http://fleet.prueba"), new("http://cargo.prueba"), new("http://billing.prueba"));

    private readonly BaseTemporal _base = new();
    private readonly ServidorFalso _identity = new();
    private readonly HttpClient _http;
    private readonly AlmacenEnMemoria _almacen = new();
    private readonly ConectividadFalsa _conectividad = new();
    private readonly RelojDePrueba _reloj = new(new DateTimeOffset(2026, 10, 27, 15, 0, 0, TimeSpan.Zero));
    private readonly ColaLocalSqlite _cola;
    private readonly GestorDeSesion _sesion;
    private readonly ServicioDePerfil _perfil;
    private int _emitidos;

    public IdentityModuloTests()
    {
        _http = new HttpClient(_identity);
        _cola = _base.Abrir(_reloj);
        var cliente = new ClienteIdentity(_http, DireccionIdentity);
        _sesion = new GestorDeSesion(_almacen, cliente, _conectividad, _cola, _reloj);
        _perfil = new ServicioDePerfil(_sesion, cliente, _cola, _almacen, _reloj);
        _identity.Responder = (s, _) => Task.FromResult(IdentityQueFunciona(s));
    }

    public void Dispose()
    {
        _http.Dispose();
        _base.Dispose();
    }

    // ---- Inicio de sesión ----------------------------------------------------------------------

    [Fact]
    public async Task Sin_senal_el_login_no_se_intenta_ni_se_guarda_para_despues()
    {
        _conectividad.HayConexion = false;

        var resultado = await _sesion.IniciarSesionAsync("carlos@x.co", "Clave-2026");

        Assert.Equal(EstadoAutenticacion.SinConexion, resultado.Estado);
        Assert.Empty(_identity.Recibidas);
        Assert.Equal(0, await _cola.ContarAsync(EstadoOperacion.PendienteSync));
        Assert.False((await _sesion.EstadoAsync()).Iniciada);
    }

    [Fact]
    public async Task El_login_guarda_los_tokens_solo_en_el_almacen_seguro()
    {
        var resultado = await _sesion.IniciarSesionAsync("  Carlos@X.co ", "Clave-2026");
        await _cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");

        Assert.True(resultado.Exitosa);
        var guardada = JsonSerializer.Deserialize<DatosDeSesion>(_almacen.Valores[ClavesDelAlmacen.Sesion], new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("acceso-1", guardada.AccessToken);
        Assert.Equal("carlos@x.co", guardada.Email);

        // Ni el access token ni el refresh token aparecen en el archivo SQLite de la cola.
        var bytes = await LeerArchivoAsync(_base.Ruta);
        Assert.DoesNotContain("acceso-1", bytes);
        Assert.DoesNotContain("refresco-1", bytes);
    }

    [Fact]
    public async Task Un_login_rechazado_da_un_mensaje_generico_y_no_deja_sesion()
    {
        _identity.Responder = (_, _) => Task.FromResult(ServidorFalso.Problema(HttpStatusCode.Unauthorized, null, "Credenciales inválidas"));

        var resultado = await _sesion.IniciarSesionAsync("carlos@x.co", "otra");

        Assert.Equal(EstadoAutenticacion.Rechazada, resultado.Estado);
        Assert.Equal("Correo o contraseña incorrectos.", resultado.Mensaje);
        Assert.False((await _sesion.EstadoAsync()).Iniciada);
    }

    // ---- El token para el motor ----------------------------------------------------------------

    [Fact]
    public async Task Con_el_token_vigente_no_se_llama_a_Identity()
    {
        await IniciarAsync();

        var token = await _sesion.ObtenerTokenVigenteAsync();

        Assert.Equal("acceso-1", token);
        Assert.Single(_identity.Recibidas);
    }

    [Fact]
    public async Task Un_token_vencido_se_renueva_con_el_refresh_token()
    {
        await IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromMinutes(15));

        var token = await _sesion.ObtenerTokenVigenteAsync();

        Assert.Equal("acceso-2", token);
        Assert.Equal("/api/auth/refresh", _identity.Recibidas[^1].Direccion.AbsolutePath);
        Assert.Contains("refresco-1", _identity.Recibidas[^1].Cuerpo);
        Assert.Equal(_reloj.GetUtcNow().UtcDateTime, (await _sesion.EstadoAsync()).UltimoContactoUtc);
    }

    [Fact]
    public async Task Se_renueva_un_minuto_antes_de_que_venza()
    {
        await IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(1));

        Assert.Equal("acceso-2", await _sesion.ObtenerTokenVigenteAsync());
    }

    [Fact]
    public async Task Vencido_y_sin_senal_no_hay_token_pero_la_sesion_sigue()
    {
        await IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromMinutes(20));
        _conectividad.HayConexion = false;

        Assert.Null(await _sesion.ObtenerTokenVigenteAsync());
        Assert.Single(_identity.Recibidas);
        Assert.True((await _sesion.EstadoAsync()).Iniciada);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Tu sesión terminó")]
    [InlineData(HttpStatusCode.Forbidden, "suspendida")]
    public async Task Si_Identity_rechaza_la_renovacion_la_sesion_se_cierra_con_su_motivo(HttpStatusCode estado, string motivo)
    {
        await IniciarAsync();
        await _almacen.GuardarAsync(ClavesDelAlmacen.Perfil, "{}");
        _reloj.Avanzar(TimeSpan.FromMinutes(20));
        _identity.Responder = (_, _) => Task.FromResult(ServidorFalso.Problema(estado, null, "no"));

        Assert.Null(await _sesion.ObtenerTokenVigenteAsync());

        var sesion = await _sesion.EstadoAsync();
        Assert.False(sesion.Iniciada);
        Assert.Contains(motivo, sesion.MotivoDeCierre);
        Assert.False(_almacen.Valores.ContainsKey(ClavesDelAlmacen.Sesion));
        Assert.False(_almacen.Valores.ContainsKey(ClavesDelAlmacen.Perfil));
    }

    [Fact]
    public async Task Si_Identity_no_responde_la_sesion_sigue_y_no_se_entrega_un_token_vencido()
    {
        await IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromMinutes(20));
        _identity.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        Assert.Null(await _sesion.ObtenerTokenVigenteAsync());
        Assert.True((await _sesion.EstadoAsync()).Iniciada);
    }

    [Fact]
    public async Task Varias_sincronizaciones_a_la_vez_renuevan_una_sola_vez()
    {
        await IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromMinutes(20));

        var tokens = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _sesion.ObtenerTokenVigenteAsync()));

        Assert.All(tokens, t => Assert.Equal("acceso-2", t));
        Assert.Single(_identity.Recibidas, r => r.Direccion.AbsolutePath == "/api/auth/refresh");
    }

    [Fact]
    public async Task Un_401_del_servidor_obliga_a_renovar_en_el_proximo_envio()
    {
        await IniciarAsync();

        await _sesion.NotificarTokenRechazadoAsync();

        Assert.Equal("acceso-2", await _sesion.ObtenerTokenVigenteAsync());
    }

    [Fact]
    public async Task Cerrar_sesion_revoca_el_refresh_token_borra_el_almacen_y_conserva_la_cola()
    {
        await IniciarAsync();
        await _perfil.CambiarPerfilAsync("Carlos Ruiz", "3001234567");

        await _sesion.CerrarSesionAsync();

        Assert.Equal("/api/auth/logout", _identity.Recibidas[^1].Direccion.AbsolutePath);
        Assert.False(_almacen.Valores.ContainsKey(ClavesDelAlmacen.Sesion));
        Assert.False(_almacen.Valores.ContainsKey(ClavesDelAlmacen.Perfil));
        Assert.Equal(1, await _cola.ContarAsync(EstadoOperacion.PendienteSync));
        Assert.Null(await _sesion.ObtenerTokenVigenteAsync());
    }

    // ---- Un teléfono compartido ----------------------------------------------------------------

    [Fact]
    public async Task Otra_cuenta_no_entra_mientras_haya_acciones_sin_enviar_de_la_anterior()
    {
        await IniciarAsync();
        await _perfil.CambiarPerfilAsync("Carlos Ruiz", "3001234567");
        await _sesion.CerrarSesionAsync();
        var llamadas = _identity.Recibidas.Count;

        var otra = await _sesion.IniciarSesionAsync("marta@x.co", "Clave-2026");

        Assert.Equal(EstadoAutenticacion.AccionesDeOtraCuenta, otra.Estado);
        Assert.Contains("ca***@x.co", otra.Mensaje);
        Assert.DoesNotContain("carlos@x.co", otra.Mensaje);
        Assert.Equal(llamadas, _identity.Recibidas.Count);
        Assert.False((await _sesion.EstadoAsync()).Iniciada);

        // La dueña sí vuelve a entrar y ve su cambio pendiente.
        await IniciarAsync();
        Assert.Equal("3001234567", (await _perfil.ObtenerVistaAsync()).Telefono);
    }

    [Fact]
    public async Task Si_la_cuenta_anterior_no_puede_volver_otra_entra_confirmando_y_esas_acciones_quedan_rechazadas_con_su_motivo()
    {
        await IniciarAsync();
        var deCarlos = await _perfil.CambiarPerfilAsync("Carlos Ruiz", "3001234567");
        var deCargo = await _cola.EncolarAsync("Cargo", "ConfirmarEntrega", "{}");
        await _sesion.CerrarSesionAsync();

        var otra = await _sesion.IniciarSesionAsync("marta@x.co", "Clave-2026", rechazarAccionesDeOtraCuenta: true);

        Assert.True(otra.Exitosa);
        foreach (var id in new[] { deCarlos.OperationId, deCargo.OperationId })
        {
            var operacion = (await _cola.ObtenerAsync(id))!;
            Assert.Equal(EstadoOperacion.Rechazado, operacion.Estado);
            Assert.Equal(TipoConflicto.B, operacion.TipoConflicto);
            Assert.Contains("ca***@x.co", operacion.MotivoRechazo);
        }

        Assert.Equal(0, await _cola.ContarAsync(EstadoOperacion.PendienteSync));
    }

    [Fact]
    public async Task Confirmar_con_credenciales_malas_no_rechaza_nada()
    {
        await IniciarAsync();
        var deCarlos = await _perfil.CambiarPerfilAsync("Carlos Ruiz", "3001234567");
        await _sesion.CerrarSesionAsync();
        _identity.Responder = (_, _) => Task.FromResult(ServidorFalso.Problema(HttpStatusCode.Unauthorized, null, "no"));

        var otra = await _sesion.IniciarSesionAsync("marta@x.co", "mala", rechazarAccionesDeOtraCuenta: true);

        Assert.Equal(EstadoAutenticacion.Rechazada, otra.Estado);
        Assert.Equal(EstadoOperacion.PendienteSync, (await _cola.ObtenerAsync(deCarlos.OperationId))!.Estado);
    }

    [Fact]
    public async Task Con_la_cola_ya_enviada_entra_otra_cuenta_y_no_ve_los_cambios_de_la_anterior()
    {
        await IniciarAsync();
        var cambio = await _perfil.CambiarPerfilAsync("Carlos Ruiz", "12ab");
        await _cola.MarcarSincronizandoAsync(cambio.OperationId);
        await _cola.RechazarAsync(cambio.OperationId, TipoConflicto.A, "Teléfono inválido.");
        await _sesion.CerrarSesionAsync();
        _reloj.Avanzar(TimeSpan.FromSeconds(1));

        var otra = await _sesion.IniciarSesionAsync("marta@x.co", "Clave-2026");
        var vista = await _perfil.ObtenerVistaAsync();

        Assert.True(otra.Exitosa);
        Assert.Equal("marta@x.co", vista.Sesion.Email);
        Assert.Null(vista.Rechazo);
        Assert.False(vista.PuedeCorregir);
        Assert.Null(vista.Telefono);
    }

    // ---- Acciones encoladas --------------------------------------------------------------------

    [Fact]
    public void Los_manejadores_arman_las_rutas_del_contrato()
    {
        var operacion = new OperacionPendiente
        {
            OperationId = Guid.NewGuid(), Modulo = "Identity", Tipo = "ActualizarPerfil",
            Payload = """{"nombre":"Carlos","telefono":"300"}""", Estado = EstadoOperacion.PendienteSync, CreadaEnUtc = DateTime.UtcNow
        };

        using var perfil = new ActualizarPerfilHandler(Servicios).Construir(operacion);
        using var cierre = new CerrarOtrasSesionesHandler(Servicios).Construir(operacion with { Tipo = "CerrarOtrasSesiones" });

        Assert.Equal(HttpMethod.Put, perfil.Method);
        Assert.Equal("http://identity.prueba/api/perfil", perfil.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, cierre.Method);
        Assert.Equal("http://identity.prueba/api/perfil/cierre-otras-sesiones", cierre.RequestUri!.ToString());

        // El registro de módulos acepta los dos sin choques.
        var registro = new RegistroDeModulos([new ModuloIdentityDePrueba()]);
        Assert.NotNull(registro.Manejador("Identity", "ActualizarPerfil"));
        Assert.NotNull(registro.Manejador("Identity", "CerrarOtrasSesiones"));
    }

    [Fact]
    public async Task El_motor_envia_el_cambio_de_perfil_con_el_token_de_la_sesion_y_su_OperationId()
    {
        await IniciarAsync();
        var operacion = await _perfil.CambiarPerfilAsync(" Carlos Ruiz ", " 300 123 4567 ");
        var motor = new MotorSincronizacion(_cola, new RegistroDeModulos([new ModuloIdentityDePrueba()]), _sesion, _conectividad, _http, _reloj);

        var resultado = await motor.SincronizarAsync();

        Assert.Equal(1, resultado.Sincronizadas);
        var enviada = _identity.Recibidas[^1];
        Assert.Equal(HttpMethod.Put, enviada.Metodo);
        Assert.Equal("Bearer acceso-1", enviada.Authorization);
        Assert.Equal(operacion.OperationId.ToString("D"), enviada.IdempotencyKey);
        Assert.Equal("""{"nombre":"Carlos Ruiz","telefono":"300 123 4567"}""", enviada.Cuerpo);
    }

    [Fact]
    public async Task Sin_sesion_el_motor_no_envia_nada_y_la_accion_espera()
    {
        await _perfil.CambiarPerfilAsync("Carlos Ruiz", null);
        var motor = new MotorSincronizacion(_cola, new RegistroDeModulos([new ModuloIdentityDePrueba()]), _sesion, _conectividad, _http, _reloj);

        var resultado = await motor.SincronizarAsync();

        Assert.Equal(ParadaSincronizacion.SinSesion, resultado.Parada);
        Assert.Empty(_identity.Recibidas);
        Assert.Equal(1, await _cola.ContarAsync(EstadoOperacion.PendienteSync));
    }

    [Fact]
    public async Task Un_nombre_vacio_no_se_encola()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _perfil.CambiarPerfilAsync("   ", "3001234567"));
        Assert.Equal(0, await _cola.ContarAsync(EstadoOperacion.PendienteSync));
    }

    // ---- Lo que muestra la pantalla ------------------------------------------------------------

    [Fact]
    public async Task Sin_senal_la_pantalla_muestra_el_perfil_guardado_con_el_cambio_pendiente_encima()
    {
        await IniciarAsync();
        Assert.True(await _perfil.RefrescarAsync());
        _conectividad.HayConexion = false;

        await _perfil.CambiarPerfilAsync("Carlos Ruiz", "3009999999");
        var vista = await _perfil.ObtenerVistaAsync();

        Assert.Equal("Carlos Ruiz", vista.Nombre);
        Assert.Equal("3009999999", vista.Telefono);
        Assert.Equal("Transportes del Cauca", vista.Organizacion);
        Assert.Equal("Conductor", vista.Role);
        Assert.Contains("Se envía cuando haya señal", vista.Aviso);
        Assert.Equal(_reloj.GetUtcNow().UtcDateTime, vista.UltimaSincronizacionUtc);
        Assert.Equal("carlos@x.co", vista.Sesion.Email);
    }

    [Fact]
    public async Task Un_rechazo_A_se_muestra_con_su_motivo_y_se_corrige_con_una_accion_nueva()
    {
        await IniciarAsync();
        var mala = await _perfil.CambiarPerfilAsync("Carlos Ruiz", "12ab");
        await _cola.MarcarSincronizandoAsync(mala.OperationId);
        await _cola.RechazarAsync(mala.OperationId, TipoConflicto.A, "El teléfono debe tener entre 7 y 15 dígitos.");

        var vista = await _perfil.ObtenerVistaAsync();
        Assert.True(vista.PuedeCorregir);
        Assert.Equal(TipoConflicto.A, vista.TipoDeRechazo);
        Assert.Contains("7 y 15", vista.Rechazo);

        var corregida = await _perfil.CorregirAsync(vista.OperacionRechazada!.Value, "Carlos Ruiz", "3001234567");

        Assert.NotEqual(mala.OperationId, corregida.OperationId);
        Assert.Equal(EstadoOperacion.Rechazado, (await _cola.ObtenerAsync(mala.OperationId))!.Estado);
        Assert.Equal("3001234567", (await _perfil.ObtenerVistaAsync()).Telefono);
    }

    [Fact]
    public async Task Un_rechazo_B_se_muestra_pero_no_se_puede_corregir()
    {
        await IniciarAsync();
        var cambio = await _perfil.CambiarPerfilAsync("Carlos Ruiz", "3001234567");
        await _cola.MarcarSincronizandoAsync(cambio.OperationId);
        await _cola.RechazarAsync(cambio.OperationId, TipoConflicto.B, "La organización está suspendida.");

        var vista = await _perfil.ObtenerVistaAsync();

        Assert.False(vista.PuedeCorregir);
        Assert.Equal(TipoConflicto.B, vista.TipoDeRechazo);
    }

    // ---- Apoyo -----------------------------------------------------------------------------------

    private async Task IniciarAsync()
    {
        Assert.True((await _sesion.IniciarSesionAsync("carlos@x.co", "Clave-2026")).Exitosa);
    }

    /// <summary>Identity falso: emite tokens numerados que vencen en 15 minutos (según el reloj de la prueba).</summary>
    private HttpResponseMessage IdentityQueFunciona(SolicitudRecibida solicitud)
    {
        switch (solicitud.Direccion.AbsolutePath)
        {
            case "/api/auth/login":
            case "/api/auth/refresh":
                var numero = Interlocked.Increment(ref _emitidos);
                var expira = _reloj.GetUtcNow().UtcDateTime.AddMinutes(15).ToString("O");
                return Json($$"""{"accessToken":"acceso-{{numero}}","refreshToken":"refresco-1","expiraEn":"{{expira}}"}""");

            case "/api/perfil" when solicitud.Metodo == HttpMethod.Get:
                return Json("""
                    {"userId":"6f9619ff-8b86-d011-b42d-00c04fc964ff","tenantId":"0e7ace00-0000-4000-8000-000000000002",
                     "nombre":"Carlos","email":"carlos@x.co","telefono":null,"role":"Conductor",
                     "organizacion":"Transportes del Cauca","estadoOrganizacion":"Activo"}
                    """);

            default:
                return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static HttpResponseMessage Json(string cuerpo) =>
        new(HttpStatusCode.OK) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static async Task<string> LeerArchivoAsync(string ruta)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var bytes = new List<byte>();
        foreach (var archivo in new[] { ruta, ruta + "-wal" }.Where(File.Exists))
        {
            await using var flujo = new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memoria = new MemoryStream();
            await flujo.CopyToAsync(memoria);
            bytes.AddRange(memoria.ToArray());
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    private sealed class ModuloIdentityDePrueba : IModuloApp
    {
        public string Nombre => "Identity";

        public string Titulo => "Perfil";

        public IReadOnlyList<IManejadorOperacion> Manejadores { get; } =
            [new ActualizarPerfilHandler(Servicios), new CerrarOtrasSesionesHandler(Servicios)];
    }
}

/// <summary>El almacén seguro de las pruebas: un diccionario en memoria.</summary>
internal sealed class AlmacenEnMemoria : IAlmacenSeguro
{
    public Dictionary<string, string> Valores { get; } = [];

    public Task<string?> LeerAsync(string clave) => Task.FromResult(Valores.GetValueOrDefault(clave));

    public Task GuardarAsync(string clave, string valor)
    {
        Valores[clave] = valor;
        return Task.CompletedTask;
    }

    public Task BorrarAsync(string clave)
    {
        Valores.Remove(clave);
        return Task.CompletedTask;
    }
}
