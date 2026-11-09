using System.Net;
using System.Text;
using System.Text.Json;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sesion;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Mobile.Tests;

internal sealed class AlmacenSeguroFalso : IAlmacenSeguro
{
    public Dictionary<string, string> Guardado { get; } = new();

    public Task<string?> LeerAsync(string clave) => Task.FromResult(Guardado.GetValueOrDefault(clave));

    public Task GuardarAsync(string clave, string valor)
    {
        Guardado[clave] = valor;
        return Task.CompletedTask;
    }

    public void Quitar(string clave) => Guardado.Remove(clave);
}

internal sealed class AutenticadorLocalFalso : IAutenticadorLocal
{
    public bool Acepta { get; set; } = true;

    public int Pedidos { get; private set; }

    public Task<bool> VerificarAsync(string motivo, CancellationToken ct = default)
    {
        Pedidos++;
        return Task.FromResult(Acepta);
    }
}

/// <summary>La sesión del conductor: iniciar, renovar, no usar nunca un token vencido y las reglas de la sesión degradada (ADR 0005).</summary>
public sealed class SesionTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Usuario = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly RelojDePrueba _reloj = new(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
    private readonly AlmacenSeguroFalso _almacen = new();
    private readonly ServidorFalso _servidor = new();
    private readonly ConectividadFalsa _conectividad = new();
    private readonly AutenticadorLocalFalso _autenticador = new();
    private readonly HttpClient _http;
    private readonly ServicioDeSesion _sesion;

    public SesionTests()
    {
        _http = new HttpClient(_servidor);
        var servicios = ConfiguracionServicios.Leer(
            """{ "entorno": "desarrollo", "servicios": { "identity": "http://identity.prueba", "fleet": "http://fleet.prueba", "cargo": "http://cargo.prueba", "billing": "http://billing.prueba" } }""");
        _sesion = new ServicioDeSesion(_almacen, _http, servicios, _conectividad, _autenticador, _reloj);
        ResponderConToken();
    }

    public void Dispose() => _http.Dispose();

    private string TokenFalso(TimeSpan duracion, string rol = "Conductor")
    {
        static string B64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exp = _reloj.GetUtcNow().Add(duracion).ToUnixTimeSeconds();
        return $"{B64("""{"alg":"RS256","typ":"JWT"}""")}.{B64($$"""{"sub":"{{Usuario}}","tenant_id":"{{Tenant}}","role":"{{rol}}","exp":{{exp}}}""")}.firma";
    }

    private void ResponderConToken(TimeSpan? duracion = null, string refresh = "refresh-1") =>
        _servidor.Responder = (_, _) => Task.FromResult(Json(HttpStatusCode.OK, new
        {
            accessToken = TokenFalso(duracion ?? TimeSpan.FromMinutes(15)),
            refreshToken = refresh,
            expiraEn = _reloj.GetUtcNow().Add(duracion ?? TimeSpan.FromMinutes(15)).UtcDateTime,
            tipo = "Bearer"
        }));

    private static HttpResponseMessage Json(HttpStatusCode estado, object cuerpo) =>
        new(estado) { Content = new StringContent(JsonSerializer.Serialize(cuerpo), Encoding.UTF8, "application/json") };

    private async Task IniciarAsync() => Assert.True((await _sesion.IniciarSesionAsync("conductor@ejemplo.co", "una-clave-larga-123")).Exito);

    // ------------------------------------------------------------------ iniciar y cerrar sesión

    [Fact]
    public async Task Iniciar_sesion_guarda_los_tokens_solo_en_el_almacen_seguro_y_lee_quien_es()
    {
        await IniciarAsync();

        var datos = (await _sesion.ActualAsync())!;
        Assert.Equal("Conductor", datos.Rol);
        Assert.Equal(Tenant, datos.TenantId);
        Assert.Equal(Usuario, datos.UserId);
        Assert.Equal("conductor@ejemplo.co", datos.Email);
        Assert.Equal(_reloj.GetUtcNow().UtcDateTime, datos.UltimoContactoUtc);
        Assert.Single(_almacen.Guardado);
        Assert.Contains("refresh-1", _almacen.Guardado.Values.Single());
        Assert.Equal(ModoDeSesion.Normal, await _sesion.ModoAsync());
    }

    [Fact]
    public async Task El_inicio_de_sesion_va_a_Identity_con_el_correo_y_la_contrasena_y_nada_se_encola()
    {
        await IniciarAsync();

        var llamada = Assert.Single(_servidor.Recibidas);
        Assert.Equal(HttpMethod.Post, llamada.Metodo);
        Assert.Equal("http://identity.prueba/api/auth/login", llamada.Direccion.ToString());
        Assert.Contains("una-clave-larga-123", llamada.Cuerpo);
        // El servicio de sesión no tiene ni cola ni forma de guardar la contraseña para reintentarla.
        Assert.DoesNotContain(typeof(ServicioDeSesion).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance),
            campo => campo.FieldType.Name.Contains("Cola", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Credenciales_invalidas_no_guardan_nada()
    {
        _servidor.Responder = (_, _) => Task.FromResult(Json(HttpStatusCode.Unauthorized, new { title = "Credenciales inválidas" }));

        var resultado = await _sesion.IniciarSesionAsync("conductor@ejemplo.co", "mal");

        Assert.False(resultado.Exito);
        Assert.Equal("Credenciales inválidas.", resultado.Mensaje);
        Assert.Empty(_almacen.Guardado);
        Assert.Equal(ModoDeSesion.SinSesion, await _sesion.ModoAsync());
    }

    [Fact]
    public async Task Sin_conexion_no_se_puede_iniciar_sesion_y_ni_se_intenta()
    {
        _conectividad.HayConexion = false;

        var resultado = await _sesion.IniciarSesionAsync("conductor@ejemplo.co", "una-clave-larga-123");

        Assert.False(resultado.Exito);
        Assert.Contains("conexión", resultado.Mensaje);
        Assert.Empty(_servidor.Recibidas);
    }

    [Fact]
    public async Task Cerrar_sesion_borra_el_almacen_y_avisa_a_Identity_para_revocar_el_refresh()
    {
        await IniciarAsync();

        await _sesion.CerrarSesionAsync();

        Assert.Empty(_almacen.Guardado);
        Assert.Equal(ModoDeSesion.SinSesion, await _sesion.ModoAsync());
        Assert.Equal("http://identity.prueba/api/auth/logout", _servidor.Recibidas.Last().Direccion.ToString());
        Assert.Contains("refresh-1", _servidor.Recibidas.Last().Cuerpo);
    }

    [Fact]
    public async Task Cerrar_sesion_sin_conexion_igual_termina_la_sesion_local()
    {
        await IniciarAsync();
        _conectividad.HayConexion = false;

        await _sesion.CerrarSesionAsync();

        Assert.Empty(_almacen.Guardado);
        Assert.Single(_servidor.Recibidas);
    }

    // ------------------------------------------------------------------ el token que se usa al sincronizar

    [Fact]
    public async Task Con_el_token_vigente_se_devuelve_sin_llamar_a_Identity()
    {
        await IniciarAsync();

        var token = await _sesion.ObtenerTokenVigenteAsync();

        Assert.NotNull(token);
        Assert.Single(_servidor.Recibidas);
    }

    [Fact]
    public async Task Con_el_token_vencido_y_conexion_se_renueva_y_se_usa_el_nuevo()
    {
        await IniciarAsync();
        var original = (await _sesion.ActualAsync())!.AccessToken;
        _reloj.Avanzar(TimeSpan.FromMinutes(20));
        ResponderConToken(refresh: "no-se-usa");

        var token = await _sesion.ObtenerTokenVigenteAsync();

        Assert.NotNull(token);
        Assert.NotEqual(original, token);
        Assert.Equal("http://identity.prueba/api/auth/refresh", _servidor.Recibidas.Last().Direccion.ToString());
        // Identity no rota el refresh token: el que la app tenía sigue siendo el suyo.
        Assert.Equal("refresh-1", (await _sesion.ActualAsync())!.RefreshToken);
        Assert.Equal(_reloj.GetUtcNow().UtcDateTime, (await _sesion.ActualAsync())!.UltimoContactoUtc);
    }

    [Fact]
    public async Task Con_el_token_vencido_y_sin_conexion_no_se_devuelve_el_viejo_ni_se_llama_a_nadie()
    {
        await IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromMinutes(20));
        _conectividad.HayConexion = false;

        var token = await _sesion.ObtenerTokenVigenteAsync();

        Assert.Null(token);
        Assert.Single(_servidor.Recibidas);
    }

    [Fact]
    public async Task Si_Identity_ya_no_reconoce_el_refresh_la_sesion_termina()
    {
        await IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromMinutes(20));
        _servidor.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var token = await _sesion.ObtenerTokenVigenteAsync();

        Assert.Null(token);
        Assert.Equal(ModoDeSesion.SinSesion, await _sesion.ModoAsync());
        Assert.Empty(_almacen.Guardado);
    }

    [Fact]
    public async Task Si_Identity_falla_al_renovar_la_sesion_se_conserva_para_intentarlo_despues()
    {
        await IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromMinutes(20));
        _servidor.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var token = await _sesion.ObtenerTokenVigenteAsync();

        Assert.Null(token);
        Assert.NotNull(await _sesion.ActualAsync());
        Assert.Single(_almacen.Guardado);
    }

    [Fact]
    public async Task Un_401_del_servidor_con_un_token_que_parecia_vigente_fuerza_una_renovacion()
    {
        await IniciarAsync();
        var llamadasAntes = _servidor.Recibidas.Count;

        await _sesion.NotificarTokenRechazadoAsync();

        Assert.Equal(llamadasAntes + 1, _servidor.Recibidas.Count);
        Assert.EndsWith("/api/auth/refresh", _servidor.Recibidas.Last().Direccion.AbsolutePath);
    }

    // ------------------------------------------------------------------ sesión degradada (48 h, sin acciones críticas)

    [Fact]
    public async Task Sin_conexion_y_con_el_token_vencido_la_sesion_pasa_a_degradada_y_se_desbloquea_con_la_huella_o_el_PIN()
    {
        await IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromHours(3));
        _conectividad.HayConexion = false;

        Assert.Equal(ModoDeSesion.Degradada, await _sesion.ModoAsync());
        // Hasta que la persona se identifique en el teléfono, no se deja trabajar.
        Assert.False(await _sesion.PuedeEncolarAsync(accionCritica: false));

        Assert.True(await _sesion.DesbloquearSesionDegradadaAsync());
        Assert.Equal(1, _autenticador.Pedidos);
        Assert.True(await _sesion.PuedeEncolarAsync(accionCritica: false));
    }

    [Fact]
    public async Task En_sesion_degradada_no_se_encolan_acciones_criticas_ni_siquiera_desbloqueada()
    {
        await IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromHours(3));
        _conectividad.HayConexion = false;
        await _sesion.DesbloquearSesionDegradadaAsync();

        Assert.False(await _sesion.PuedeEncolarAsync(accionCritica: true));
        Assert.True(await _sesion.PuedeEncolarAsync(accionCritica: false));
    }

    [Fact]
    public async Task Si_la_persona_no_pasa_la_verificacion_local_la_sesion_degradada_sigue_bloqueada()
    {
        await IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromHours(3));
        _conectividad.HayConexion = false;
        _autenticador.Acepta = false;

        Assert.False(await _sesion.DesbloquearSesionDegradadaAsync());
        Assert.False(await _sesion.PuedeEncolarAsync(accionCritica: false));
    }

    [Fact]
    public async Task Pasadas_las_48_horas_sin_contacto_con_Identity_la_sesion_expira()
    {
        await IniciarAsync();
        _conectividad.HayConexion = false;

        _reloj.Avanzar(TimeSpan.FromHours(48));
        Assert.Equal(ModoDeSesion.Degradada, await _sesion.ModoAsync());

        _reloj.Avanzar(TimeSpan.FromSeconds(1));
        Assert.Equal(ModoDeSesion.Expirada, await _sesion.ModoAsync());
        Assert.False(await _sesion.PuedeEncolarAsync(accionCritica: false));
    }

    [Fact]
    public async Task El_limite_de_48_horas_se_cuenta_desde_el_ultimo_contacto_real_y_se_reinicia_al_renovar()
    {
        await IniciarAsync();
        _reloj.Avanzar(TimeSpan.FromHours(40));
        _conectividad.HayConexion = true;
        ResponderConToken();
        Assert.NotNull(await _sesion.ObtenerTokenVigenteAsync()); // contacto real con Identity a las 40 horas

        _conectividad.HayConexion = false;
        _reloj.Avanzar(TimeSpan.FromHours(20)); // 60 horas desde el inicio, pero 20 desde el último contacto

        Assert.Equal(ModoDeSesion.Degradada, await _sesion.ModoAsync());
    }

    [Theory]
    [InlineData(ModoDeSesion.Normal, false, true)]
    [InlineData(ModoDeSesion.Normal, true, true)]
    [InlineData(ModoDeSesion.Degradada, false, true)]
    [InlineData(ModoDeSesion.Degradada, true, false)]
    [InlineData(ModoDeSesion.Expirada, false, false)]
    [InlineData(ModoDeSesion.SinSesion, false, false)]
    public void La_politica_de_que_se_puede_encolar_en_cada_modo(ModoDeSesion modo, bool critica, bool esperado) =>
        Assert.Equal(esperado, PoliticaDeSesion.PuedeEncolar(modo, critica));

    [Fact]
    public void Con_conexion_y_el_token_vencido_la_sesion_se_considera_expirada_hasta_que_se_renueve()
    {
        var sesion = new DatosDeSesion("t", "r", DateTime.UtcNow.AddMinutes(-1), "a@b.co", "Conductor", Tenant, Usuario, DateTime.UtcNow.AddMinutes(-16));

        Assert.Equal(ModoDeSesion.Expirada, PoliticaDeSesion.Evaluar(sesion, DateTime.UtcNow, hayConexion: true));
        Assert.Equal(ModoDeSesion.SinSesion, PoliticaDeSesion.Evaluar(null, DateTime.UtcNow, hayConexion: true));
    }

    [Fact]
    public void Los_mensajes_explican_por_que_no_se_puede()
    {
        Assert.Contains("no se pueden confirmar entregas", PoliticaDeSesion.Explicar(ModoDeSesion.Degradada, accionCritica: true));
        Assert.Equal(string.Empty, PoliticaDeSesion.Explicar(ModoDeSesion.Degradada, accionCritica: false));
        Assert.Contains("Inicia sesión", PoliticaDeSesion.Explicar(ModoDeSesion.SinSesion, accionCritica: false));
    }
}

/// <summary>Lo que la pantalla muestra siempre: lo último del servidor, o lo guardado con su fecha (ADR 0005).</summary>
public sealed class CacheTests : IDisposable
{
    private readonly BaseTemporal _base = new();
    private readonly RelojDePrueba _reloj = new(new DateTimeOffset(2026, 10, 14, 15, 30, 0, TimeSpan.Zero));
    private readonly ServidorFalso _servidor = new();
    private readonly ConectividadFalsa _conectividad = new();
    private readonly SesionFalsa _sesion = new();
    private readonly HttpClient _http;
    private readonly LectorConCache _lector;
    private readonly Core.Cola.ColaLocalSqlite _cola;

    public CacheTests()
    {
        _http = new HttpClient(_servidor);
        _cola = _base.Abrir(_reloj);
        _lector = new LectorConCache(_http, _sesion, _cola, _conectividad, _reloj);
    }

    public void Dispose()
    {
        _http.Dispose();
        _base.Dispose();
    }

    private static readonly Uri Direccion = new("http://cargo.prueba/api/cargas");

    private void Responde(string json) =>
        _servidor.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    private sealed record Carga(string Descripcion, int PesoKg);

    [Fact]
    public async Task Un_dato_que_llega_del_servidor_cuenta_como_la_ultima_sincronizacion_del_banner()
    {
        Assert.Null(await _cola.UltimaSincronizacionAsync());
        Responde("""[{"descripcion":"Lodos","pesoKg":3100}]""");

        await _lector.LeerAsync<List<Carga>>("cargas", Direccion);

        Assert.Equal(_reloj.GetUtcNow().UtcDateTime, await _cola.UltimaSincronizacionAsync());
        _reloj.Avanzar(TimeSpan.FromHours(1));
        _conectividad.HayConexion = false;
        await _lector.LeerAsync<List<Carga>>("cargas", Direccion);
        Assert.Equal(_reloj.GetUtcNow().UtcDateTime.AddHours(-1), await _cola.UltimaSincronizacionAsync());
    }

    [Theory]
    [InlineData(true, true, "Actualizado hoy 3:30 p. m.")]
    [InlineData(false, true, "Datos guardados de hoy 3:30 p. m.")]
    [InlineData(true, false, "Actualizado el 14/10 3:30 p. m.")]
    [InlineData(false, false, "Datos guardados del 14/10 3:30 p. m.")]
    public void El_texto_de_actualizacion_dice_hoy_o_la_fecha_sin_errores_de_redaccion(bool delServidor, bool esHoy, string esperado)
    {
        var dato = new DateTime(2026, 10, 14, 15, 30, 0, DateTimeKind.Utc);
        var ahora = esHoy ? new DateTime(2026, 10, 14, 20, 0, 0, DateTimeKind.Utc) : new DateTime(2026, 10, 15, 20, 0, 0, DateTimeKind.Utc);

        Assert.Equal(esperado, TextosDeDatos.Actualizacion(delServidor, dato, ahora, TimeZoneInfo.Utc));
    }

    [Fact]
    public async Task Con_conexion_lee_del_servidor_con_el_token_y_lo_guarda()
    {
        Responde("""[{"descripcion":"Lodos","pesoKg":3100}]""");

        var datos = await _lector.LeerAsync<List<Carga>>("cargas", Direccion);

        Assert.True(datos.DelServidor);
        Assert.Equal("Lodos", datos.Datos!.Single().Descripcion);
        Assert.Equal(_reloj.GetUtcNow().UtcDateTime, datos.ActualizadosUtc);
        Assert.Equal("Bearer token-vigente", _servidor.Recibidas.Single().Authorization);
        Assert.NotNull(await _cola.LeerCacheAsync("cargas"));
    }

    [Fact]
    public async Task Sin_conexion_muestra_lo_guardado_con_su_fecha_y_no_llama_a_nadie()
    {
        Responde("""[{"descripcion":"Lodos","pesoKg":3100}]""");
        await _lector.LeerAsync<List<Carga>>("cargas", Direccion);
        _conectividad.HayConexion = false;
        _reloj.Avanzar(TimeSpan.FromHours(5));
        var llamadas = _servidor.Recibidas.Count;

        var datos = await _lector.LeerAsync<List<Carga>>("cargas", Direccion);

        Assert.False(datos.DelServidor);
        Assert.Equal("Lodos", datos.Datos!.Single().Descripcion);
        Assert.Equal(new DateTime(2026, 10, 14, 15, 30, 0, DateTimeKind.Utc), datos.ActualizadosUtc);
        Assert.Equal(llamadas, _servidor.Recibidas.Count);
    }

    [Fact]
    public async Task Sin_conexion_y_sin_nada_guardado_lo_dice_en_vez_de_dejar_la_pantalla_en_blanco()
    {
        _conectividad.HayConexion = false;

        var datos = await _lector.LeerAsync<List<Carga>>("cargas", Direccion);

        Assert.False(datos.HayDatos);
        Assert.Contains("Todavía no hay datos guardados", datos.Aviso);
    }

    [Fact]
    public async Task Si_el_servidor_falla_se_muestra_lo_guardado_con_un_aviso()
    {
        Responde("""[{"descripcion":"Lodos","pesoKg":3100}]""");
        await _lector.LeerAsync<List<Carga>>("cargas", Direccion);
        _servidor.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var datos = await _lector.LeerAsync<List<Carga>>("cargas", Direccion);

        Assert.True(datos.HayDatos);
        Assert.False(datos.DelServidor);
        Assert.Contains("Mostrando datos guardados", datos.Aviso);
    }

    [Fact]
    public async Task Si_el_rol_no_tiene_acceso_lo_dice_y_no_pisa_lo_guardado()
    {
        _servidor.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));

        var datos = await _lector.LeerAsync<List<Carga>>("cargas", Direccion);

        Assert.Contains("rol", datos.Aviso);
        Assert.Null(await _cola.LeerCacheAsync("cargas"));
    }

    [Fact]
    public async Task Sin_token_vigente_no_se_pide_nada_y_se_muestra_lo_guardado()
    {
        Responde("""[{"descripcion":"Lodos","pesoKg":3100}]""");
        await _lector.LeerAsync<List<Carga>>("cargas", Direccion);
        _sesion.Token = null;
        var llamadas = _servidor.Recibidas.Count;

        var datos = await _lector.LeerAsync<List<Carga>>("cargas", Direccion);

        Assert.True(datos.HayDatos);
        Assert.Contains("sesión", datos.Aviso);
        Assert.Equal(llamadas, _servidor.Recibidas.Count);
    }

    [Fact]
    public async Task El_cache_sobrevive_a_cerrar_y_abrir_la_app_y_se_actualiza_por_clave()
    {
        Responde("""[{"descripcion":"Uno","pesoKg":1}]""");
        await _lector.LeerAsync<List<Carga>>("cargas", Direccion);
        Responde("""[{"descripcion":"Dos","pesoKg":2}]""");
        await _lector.LeerAsync<List<Carga>>("cargas", Direccion);

        var reabierta = await _base.Abrir().LeerCacheAsync("cargas");

        Assert.Contains("Dos", reabierta!.Json);
        Assert.DoesNotContain("Uno", reabierta.Json);
    }

    [Fact]
    public void Una_accion_desconocida_se_trata_como_critica_y_una_marcada_como_tal_tambien()
    {
        var registro = new RegistroDeModulos([new ModuloDePrueba("Cargo", "ConfirmarEntrega")]);

        Assert.True(registro.EsCritica("Cargo", "NoExiste"));
        Assert.False(registro.EsCritica("Cargo", "ConfirmarEntrega"));
    }
}
