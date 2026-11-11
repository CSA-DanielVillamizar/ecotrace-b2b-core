using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Mobile.Core.Sesion;

/// <summary>
/// Dónde se guardan los secretos de la sesión. En la app es <c>SecureStorage</c> (Keystore de Android). A propósito es una
/// interfaz aparte de la cola local: <b>un token nunca va a SQLite, a <c>Preferences</c> ni a texto plano</b> (ADR 0005).
/// </summary>
public interface IAlmacenSeguro
{
    Task<string?> LeerAsync(string clave);

    Task GuardarAsync(string clave, string valor);

    void Quitar(string clave);
}

/// <summary>
/// Pide a la persona que se identifique en el teléfono (huella, rostro o el PIN del dispositivo). Abre la sesión degradada
/// sin conexión. Si el emulador no tiene biometría, se acepta el PIN o el patrón del dispositivo.
/// </summary>
public interface IAutenticadorLocal
{
    Task<bool> VerificarAsync(string motivo, CancellationToken ct = default);
}

/// <summary>Los datos de la sesión guardada. Es lo que se serializa dentro del almacén seguro.</summary>
public sealed record DatosDeSesion(
    string AccessToken, string RefreshToken, DateTime AccessExpiraUtc,
    string Email, string Rol, Guid TenantId, Guid UserId, DateTime UltimoContactoUtc);

public enum ModoDeSesion
{
    /// <summary>No hay sesión: hay que iniciar sesión (y eso necesita conexión).</summary>
    SinSesion = 0,

    /// <summary>Token vigente.</summary>
    Normal = 1,

    /// <summary>
    /// Token vencido y sin conexión, dentro de las 48 horas desde el último contacto real con Identity. Se puede trabajar,
    /// pero no con acciones que muevan dinero ni cierren un ciclo de negocio crítico.
    /// </summary>
    Degradada = 2,

    /// <summary>Token vencido, y o pasaron las 48 horas o ya hay conexión pero Identity no renovó: hay que volver a iniciar sesión.</summary>
    Expirada = 3
}

/// <summary>Las reglas de la sesión sin conexión del ADR 0005. Funciones puras: se prueban sin teléfono.</summary>
public static class PoliticaDeSesion
{
    /// <summary>Cuánto dura como máximo una sesión degradada, contado desde el último contacto real con Identity.</summary>
    public static readonly TimeSpan LimiteDeSesionDegradada = TimeSpan.FromHours(48);

    public static ModoDeSesion Evaluar(DatosDeSesion? sesion, DateTime ahoraUtc, bool hayConexion)
    {
        if (sesion is null)
        {
            return ModoDeSesion.SinSesion;
        }

        if (ahoraUtc < sesion.AccessExpiraUtc)
        {
            return ModoDeSesion.Normal;
        }

        // El token venció. Con conexión se intenta renovar; si no se pudo, hay que iniciar sesión otra vez.
        if (hayConexion)
        {
            return ModoDeSesion.Expirada;
        }

        return ahoraUtc - sesion.UltimoContactoUtc <= LimiteDeSesionDegradada
            ? ModoDeSesion.Degradada
            : ModoDeSesion.Expirada;
    }

    /// <summary>
    /// Qué se puede encolar en cada modo. Con sesión degradada, nada que mueva dinero ni cierre un ciclo crítico; sin sesión
    /// o con la sesión expirada, nada: no hay quién autorice la acción al sincronizar.
    /// </summary>
    public static bool PuedeEncolar(ModoDeSesion modo, bool accionCritica) => modo switch
    {
        ModoDeSesion.Normal => true,
        ModoDeSesion.Degradada => !accionCritica,
        _ => false
    };

    public static string Explicar(ModoDeSesion modo, bool accionCritica) => modo switch
    {
        ModoDeSesion.SinSesion => "Inicia sesión para continuar.",
        ModoDeSesion.Expirada => "Tu sesión terminó. Conéctate a internet e inicia sesión de nuevo.",
        ModoDeSesion.Degradada when accionCritica =>
            "Sin conexión y con la sesión vencida no se pueden confirmar entregas ni mover dinero. Se habilitará al reconectar.",
        _ => string.Empty
    };
}

/// <summary>El resultado de intentar iniciar sesión.</summary>
public sealed record ResultadoDeInicio(bool Exito, string Mensaje)
{
    public static readonly ResultadoDeInicio Correcto = new(true, string.Empty);
}

/// <summary>
/// La sesión del conductor: inicia sesión en Identity, guarda los tokens en el almacén seguro, los renueva y decide en qué
/// modo está la sesión. Es también el <see cref="IProveedorDeSesion"/> del motor de sincronización, que solo envía con un token
/// <b>vigente</b>: una acción hecha con la sesión vencida se autoriza, al sincronizar, con el token nuevo, nunca con el viejo.
/// <para>El inicio de sesión <b>no se encola jamás</b>: la app no guarda la contraseña para reintentarla.</para>
/// </summary>
public sealed class ServicioDeSesion : IProveedorDeSesion
{
    private const string Clave = "ecotrace.sesion";
    private static readonly TimeSpan MargenAntesDeVencer = TimeSpan.FromSeconds(60);

    private readonly IAlmacenSeguro _almacen;
    private readonly HttpClient _http;
    private readonly ConfiguracionServicios _servicios;
    private readonly IConectividad _conectividad;
    private readonly IAutenticadorLocal _autenticador;
    private readonly TimeProvider _reloj;
    private readonly SemaphoreSlim _renovacion = new(1, 1);
    private DatosDeSesion? _cache;
    private bool _cargada;
    private bool _degradadaDesbloqueada;

    public ServicioDeSesion(
        IAlmacenSeguro almacen, HttpClient http, ConfiguracionServicios servicios, IConectividad conectividad,
        IAutenticadorLocal autenticador, TimeProvider? reloj = null)
    {
        _almacen = almacen;
        _http = http;
        _servicios = servicios;
        _conectividad = conectividad;
        _autenticador = autenticador;
        _reloj = reloj ?? TimeProvider.System;
    }

    public event EventHandler? Cambio;

    public async Task<DatosDeSesion?> ActualAsync()
    {
        if (!_cargada)
        {
            var texto = await _almacen.LeerAsync(Clave);
            _cache = string.IsNullOrEmpty(texto) ? null : JsonSerializer.Deserialize<DatosDeSesion>(texto);
            _cargada = true;
        }

        return _cache;
    }

    public async Task<ModoDeSesion> ModoAsync() =>
        PoliticaDeSesion.Evaluar(await ActualAsync(), _reloj.GetUtcNow().UtcDateTime, _conectividad.HayConexion);

    /// <summary>¿Se puede encolar esta acción ahora? En sesión degradada hace falta haber desbloqueado con la huella o el PIN.</summary>
    public async Task<bool> PuedeEncolarAsync(bool accionCritica)
    {
        var modo = await ModoAsync();
        if (modo == ModoDeSesion.Degradada && !_degradadaDesbloqueada)
        {
            return false;
        }

        return PoliticaDeSesion.PuedeEncolar(modo, accionCritica);
    }

    /// <summary>Con la sesión degradada, pide la huella o el PIN del dispositivo antes de dejar trabajar.</summary>
    public async Task<bool> DesbloquearSesionDegradadaAsync(CancellationToken ct = default)
    {
        if (await ModoAsync() != ModoDeSesion.Degradada)
        {
            return false;
        }

        _degradadaDesbloqueada = await _autenticador.VerificarAsync("Confirma que eres tú para seguir sin conexión", ct);
        Cambio?.Invoke(this, EventArgs.Empty);
        return _degradadaDesbloqueada;
    }

    public async Task<ResultadoDeInicio> IniciarSesionAsync(string email, string password, CancellationToken ct = default)
    {
        if (!_conectividad.HayConexion)
        {
            return new(false, "Para iniciar sesión necesitas conexión a internet.");
        }

        try
        {
            using var limite = ConLimite(ct);
            using var respuesta = await _http.PostAsJsonAsync(
                new Uri(_servicios.Identity, "/api/auth/login"), new { email = email.Trim(), password }, limite.Token);

            if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new(false, "Credenciales inválidas.");
            }

            if (!respuesta.IsSuccessStatusCode)
            {
                return new(false, "No se pudo iniciar sesión. Intenta de nuevo en un momento.");
            }

            var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaDeToken>(limite.Token);
            await GuardarAsync(DesdeToken(email.Trim(), cuerpo!));
            return ResultadoDeInicio.Correcto;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new(false, "No se pudo conectar con el servidor.");
        }
    }

    public async Task CerrarSesionAsync(CancellationToken ct = default)
    {
        var sesion = await ActualAsync();
        _degradadaDesbloqueada = false;
        await LimpiarAsync();

        if (sesion is not null && _conectividad.HayConexion)
        {
            try
            {
                using var limite = ConLimite(ct);
                using var _ = await _http.PostAsJsonAsync(
                    new Uri(_servicios.Identity, "/api/auth/logout"), new { refreshToken = sesion.RefreshToken }, limite.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Sin Identity la sesión local ya terminó; el refresh token vence solo a los 7 días.
            }
        }
    }

    public async Task<string?> ObtenerTokenVigenteAsync(CancellationToken ct = default)
    {
        var sesion = await ActualAsync();
        if (sesion is null)
        {
            return null;
        }

        if (sesion.AccessExpiraUtc - _reloj.GetUtcNow().UtcDateTime > MargenAntesDeVencer)
        {
            return sesion.AccessToken;
        }

        // Vencido o por vencer: se renueva si hay conexión. Sin conexión no se devuelve el token viejo: no se usa uno vencido.
        return _conectividad.HayConexion && await RenovarAsync(ct) ? _cache?.AccessToken : null;
    }

    public async Task NotificarTokenRechazadoAsync(CancellationToken ct = default)
    {
        // El servidor dijo 401 con un token que creíamos vigente: se fuerza una renovación; si Identity tampoco la acepta, la sesión terminó.
        if (_conectividad.HayConexion)
        {
            await RenovarAsync(ct, forzar: true);
        }
    }

    private async Task<bool> RenovarAsync(CancellationToken ct, bool forzar = false)
    {
        await _renovacion.WaitAsync(ct);
        try
        {
            var sesion = await ActualAsync();
            if (sesion is null)
            {
                return false;
            }

            // Otra llamada ya la renovó mientras se esperaba el candado.
            if (!forzar && sesion.AccessExpiraUtc - _reloj.GetUtcNow().UtcDateTime > MargenAntesDeVencer)
            {
                return true;
            }

            using var limite = ConLimite(ct);
            using var respuesta = await _http.PostAsJsonAsync(
                new Uri(_servicios.Identity, "/api/auth/refresh"), new { refreshToken = sesion.RefreshToken }, limite.Token);

            if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Identity no reconoce el refresh token (vencido, revocado, organización suspendida): la sesión terminó.
                await LimpiarAsync();
                return false;
            }

            if (!respuesta.IsSuccessStatusCode)
            {
                return false;
            }

            var cuerpo = (await respuesta.Content.ReadFromJsonAsync<RespuestaDeToken>(limite.Token))!;
            await GuardarAsync(DesdeToken(sesion.Email, cuerpo with { RefreshToken = sesion.RefreshToken }));
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
        finally
        {
            _renovacion.Release();
        }
    }

    /// <summary>Un tiempo máximo para hablar con Identity: sin él, una red a medias deja la pantalla esperando para siempre.</summary>
    private static CancellationTokenSource ConLimite(CancellationToken ct)
    {
        var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(10));
        return limite;
    }

    private DatosDeSesion DesdeToken(string email, RespuestaDeToken r)
    {
        var claims = LeerClaims(r.AccessToken);
        return new DatosDeSesion(
            r.AccessToken, r.RefreshToken, r.ExpiraEn.ToUniversalTime(), email,
            claims.GetProperty("role").GetString() ?? string.Empty,
            Guid.Parse(claims.GetProperty("tenant_id").GetString()!),
            Guid.Parse(claims.GetProperty("sub").GetString()!),
            // Un contacto real con Identity: de aquí cuentan las 48 horas de la sesión degradada.
            _reloj.GetUtcNow().UtcDateTime);
    }

    private async Task GuardarAsync(DatosDeSesion sesion)
    {
        _cache = sesion;
        _cargada = true;
        await _almacen.GuardarAsync(Clave, JsonSerializer.Serialize(sesion));
        Cambio?.Invoke(this, EventArgs.Empty);
    }

    private async Task LimpiarAsync()
    {
        _cache = null;
        _cargada = true;
        _degradadaDesbloqueada = false;
        _almacen.Quitar(Clave);
        await Task.CompletedTask;
        Cambio?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Los claims del JWT, solo para mostrar quién es: la validación de verdad la hacen los servicios, con la firma.</summary>
    internal static JsonElement LeerClaims(string token)
    {
        var carga = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        carga = carga.PadRight(carga.Length + (4 - carga.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(carga))).RootElement.Clone();
    }

    private sealed record RespuestaDeToken(string AccessToken, string RefreshToken, DateTime ExpiraEn);
}
