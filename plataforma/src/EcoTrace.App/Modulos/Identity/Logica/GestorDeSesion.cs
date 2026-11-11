using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Identity.Logica;

/// <summary>Lo que la pantalla puede saber de la sesión. Nunca incluye los tokens.</summary>
public sealed record EstadoDeSesion(
    bool Iniciada,
    string? Email,
    DateTime? UltimoLoginUtc,
    DateTime? UltimoContactoUtc,
    bool AccesoVigente,
    string? MotivoDeCierre,
    ModoDeSesion Modo = ModoDeSesion.SinSesion,
    bool Desbloqueada = false,
    DateTime? DegradadaHastaUtc = null);

/// <summary>
/// La sesión del conductor (ADR 0004 y 0005). Es el <see cref="IProveedorDeSesion"/> que usa el motor de
/// sincronización de los cuatro módulos: les da el access token vigente, lo renueva con el refresh token cuando
/// vence y <b>nunca</b> devuelve uno vencido. Los tokens viven solo en el almacén seguro.
/// <para>
/// También decide si se puede encolar (ver <see cref="PoliticaDeSesion"/>). Con el token vencido y sin poder renovarlo, la
/// sesión queda <see cref="ModoDeSesion.Degradada"/> hasta 48 horas desde el último contacto con Identity: se sigue
/// trabajando tras desbloquear con huella o PIN, y lo encolado sale después con el token nuevo.
/// </para>
/// </summary>
public sealed class GestorDeSesion : IProveedorDeSesion
{
    /// <summary>Se renueva un poco antes de que venza, para que no expire en medio de un envío.</summary>
    public static readonly TimeSpan MargenDeRenovacion = TimeSpan.FromSeconds(60);

    private readonly IAlmacenSeguro _almacen;
    private readonly ClienteIdentity _cliente;
    private readonly IConectividad _conectividad;
    private readonly IColaLocal _cola;
    private readonly IAutenticadorLocal _autenticador;
    private readonly TimeProvider _reloj;
    private readonly SemaphoreSlim _candado = new(1, 1);

    // El desbloqueo vale para un último contacto concreto: si la sesión se renueva y luego vuelve a quedar degradada,
    // se pide otra vez. Vive solo en memoria: al reabrir la app hay que desbloquear de nuevo.
    private (string Email, DateTime UltimoContactoUtc)? _desbloqueo;

    public GestorDeSesion(
        IAlmacenSeguro almacen, ClienteIdentity cliente, IConectividad conectividad, IColaLocal cola,
        IAutenticadorLocal autenticador, TimeProvider? reloj = null)
    {
        _almacen = almacen;
        _cliente = cliente;
        _conectividad = conectividad;
        _cola = cola;
        _autenticador = autenticador;
        _reloj = reloj ?? TimeProvider.System;
    }

    /// <summary>Cambió la sesión: se inició, se renovó, se desbloqueó o se cerró.</summary>
    public event EventHandler? Cambio;

    /// <summary>Por qué se cerró la sesión sin que el conductor lo pidiera (venció, organización suspendida...).</summary>
    public string? MotivoDeCierre { get; private set; }

    public async Task<EstadoDeSesion> EstadoAsync()
    {
        var sesion = await _almacen.LeerAsync<DatosDeSesion>(ClavesDelAlmacen.Sesion);
        if (sesion is null)
        {
            return new EstadoDeSesion(false, null, null, null, false, MotivoDeCierre);
        }

        var modo = PoliticaDeSesion.Evaluar(sesion, Ahora);
        return new EstadoDeSesion(true, sesion.Email, sesion.UltimoLoginUtc, sesion.UltimoContactoUtc,
            EsVigente(sesion, Ahora), MotivoDeCierre, modo, EstaDesbloqueada(sesion),
            sesion.UltimoContactoUtc + PoliticaDeSesion.LimiteDegradada);
    }

    /// <summary>
    /// El modo de la sesión. Con <paramref name="intentarRenovar"/> y señal, primero intenta renovar un token vencido: así
    /// "degradada" significa que de verdad no se pudo (sin señal, o Identity no respondió). El intento dura como mucho
    /// <see cref="EsperaMaximaDeRenovacion"/> y, si falla, no se repite durante <see cref="PausaTrasFalloDeRenovacion"/>:
    /// encolar nunca puede quedarse esperando a la red.
    /// </summary>
    public async Task<ModoDeSesion> ModoAsync(bool intentarRenovar = false, CancellationToken ct = default)
    {
        var sesion = await _almacen.LeerAsync<DatosDeSesion>(ClavesDelAlmacen.Sesion);
        if (intentarRenovar && sesion is not null && !EsVigente(sesion, Ahora) && _conectividad.HayConexion
            && (_ultimoFalloDeRenovacion is not { } fallo || Ahora - fallo > PausaTrasFalloDeRenovacion))
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(EsperaMaximaDeRenovacion);
            try
            {
                await ObtenerTokenVigenteAsync(limite.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Identity tardó demasiado: se decide con lo que hay y el motor reintenta la renovación después.
            }

            sesion = await _almacen.LeerAsync<DatosDeSesion>(ClavesDelAlmacen.Sesion);
            _ultimoFalloDeRenovacion = sesion is not null && !EsVigente(sesion, Ahora) ? Ahora : null;
        }

        return PoliticaDeSesion.Evaluar(sesion, Ahora);
    }

    /// <summary>Cuánto se espera a Identity al encolar con el token vencido antes de tratar la sesión como degradada.</summary>
    public static readonly TimeSpan EsperaMaximaDeRenovacion = TimeSpan.FromSeconds(2);

    /// <summary>Tras una renovación fallida al encolar, cuánto se espera antes de volver a intentarla desde aquí.</summary>
    public static readonly TimeSpan PausaTrasFalloDeRenovacion = TimeSpan.FromSeconds(30);

    private DateTime? _ultimoFalloDeRenovacion;

    /// <summary>Lanza <see cref="AccionNoPermitidaException"/> si la sesión no permite encolar esa acción ahora.</summary>
    public async Task ExigirPermisoParaEncolarAsync(string modulo, string tipo, CancellationToken ct = default)
    {
        var modo = await ModoAsync(intentarRenovar: true, ct);
        var sesion = await _almacen.LeerAsync<DatosDeSesion>(ClavesDelAlmacen.Sesion);
        var impedimento = PoliticaDeSesion.Impedimento(modo, sesion is not null && EstaDesbloqueada(sesion), PoliticaDeSesion.EsCritica(modulo, tipo));
        if (impedimento is not null)
        {
            throw new AccionNoPermitidaException(impedimento);
        }
    }

    /// <summary>Con la sesión degradada, abre el cuadro del sistema (huella o PIN) y, si pasa, deja seguir sin conexión.</summary>
    public async Task<ResultadoDesbloqueo> DesbloquearAsync(CancellationToken ct = default)
    {
        var sesion = await _almacen.LeerAsync<DatosDeSesion>(ClavesDelAlmacen.Sesion);
        if (sesion is null || PoliticaDeSesion.Evaluar(sesion, Ahora) != ModoDeSesion.Degradada)
        {
            return ResultadoDesbloqueo.NoAplica;
        }

        if (EstaDesbloqueada(sesion))
        {
            return ResultadoDesbloqueo.Desbloqueada;
        }

        var resultado = await _autenticador.VerificarAsync("Confirma que eres tú para seguir sin conexión", ct);
        if (resultado == ResultadoDesbloqueo.Desbloqueada)
        {
            _desbloqueo = (sesion.Email, sesion.UltimoContactoUtc);
            Cambio?.Invoke(this, EventArgs.Empty);
        }

        return resultado;
    }

    private bool EstaDesbloqueada(DatosDeSesion sesion) =>
        _desbloqueo is { } d && d.Email == sesion.Email && d.UltimoContactoUtc == sesion.UltimoContactoUtc;

    /// <summary>
    /// Inicia sesión. Necesita señal: el login nunca se encola ni se guarda la contraseña.
    /// <para>
    /// Si en el teléfono quedan acciones sin enviar de otra cuenta, no deja entrar a una cuenta distinta: se enviarían
    /// con su token. Solo con <paramref name="rechazarAccionesDeOtraCuenta"/> (el conductor lo confirmó en pantalla) y
    /// después de que Identity valide las credenciales, esas acciones pasan a <c>Rechazado</c> (B) con su motivo. No se
    /// borran ni se envían: es la salida cuando la cuenta anterior ya no puede entrar (organización suspendida, retiro).
    /// </para>
    /// </summary>
    public async Task<ResultadoAutenticacion> IniciarSesionAsync(
        string? email, string? password, bool rechazarAccionesDeOtraCuenta = false, CancellationToken ct = default)
    {
        var correo = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (correo.Length == 0 || string.IsNullOrEmpty(password))
        {
            return new(EstadoAutenticacion.Rechazada, null, "Escribe tu correo y tu contraseña.");
        }

        var dueno = await _almacen.LeerAsync<DuenoDeLaCola>(ClavesDelAlmacen.DuenoDeLaCola);
        var deOtraCuenta = dueno is not null && dueno.Email != correo ? await PendientesAsync(ct) : 0;
        if (deOtraCuenta > 0 && !rechazarAccionesDeOtraCuenta)
        {
            return new(EstadoAutenticacion.AccionesDeOtraCuenta, null,
                $"Este teléfono tiene {deOtraCuenta} {(deOtraCuenta == 1 ? "acción" : "acciones")} sin enviar de " +
                $"{Enmascarar(dueno!.Email)}. Esa cuenta debe iniciar sesión para enviarlas antes de que entre otra.");
        }

        if (!_conectividad.HayConexion)
        {
            return new(EstadoAutenticacion.SinConexion, null,
                "Para iniciar sesión necesitas señal. El inicio de sesión no se guarda para después.");
        }

        var resultado = await _cliente.IniciarSesionAsync(correo, password, ct);
        if (!resultado.Exitosa)
        {
            return resultado;
        }

        await _candado.WaitAsync(ct);
        try
        {
            if (deOtraCuenta > 0)
            {
                await RechazarAccionesDeAsync(dueno!.Email);
            }

            var ahora = Ahora;
            var tokens = resultado.Tokens!;
            await _almacen.GuardarAsync(ClavesDelAlmacen.Sesion,
                new DatosDeSesion(tokens.AccessToken, tokens.RefreshToken, tokens.ExpiraEnUtc, correo, ahora, ahora));

            if (dueno?.Email != correo)
            {
                // Cambió la cuenta: lo que haya en la cola de antes ya se envió o se rechazó, y no es de esta.
                await _almacen.GuardarAsync(ClavesDelAlmacen.DuenoDeLaCola, new DuenoDeLaCola(correo, ahora));
                await _almacen.BorrarAsync(ClavesDelAlmacen.Perfil);
            }

            MotivoDeCierre = null;
        }
        finally
        {
            _candado.Release();
        }

        Cambio?.Invoke(this, EventArgs.Empty);
        return resultado;
    }

    private async Task<int> PendientesAsync(CancellationToken ct) =>
        await _cola.ContarAsync(EstadoOperacion.PendienteSync, ct) + await _cola.ContarAsync(EstadoOperacion.Sincronizando, ct);

    /// <summary>
    /// Pasa a Rechazado (B) las acciones sin enviar de la cuenta anterior, con un motivo que dice por qué. Quedan en la
    /// cola para auditoría: nunca se pierden en silencio ni salen con el token de otra cuenta.
    /// </summary>
    private async Task RechazarAccionesDeAsync(string emailAnterior)
    {
        // Sin sesión el motor no envía nada, así que lo que esté "Sincronizando" quedó de un envío interrumpido.
        await _cola.RecuperarInterrumpidasAsync();
        var motivo = $"No se envió: la cuenta que la hizo ({Enmascarar(emailAnterior)}) no volvió a iniciar sesión " +
                     "en este teléfono y entró otra cuenta. Si aún aplica, esa cuenta debe repetirla.";

        foreach (var operacion in await _cola.ListarAsync(EstadoOperacion.PendienteSync))
        {
            await _cola.MarcarSincronizandoAsync(operacion.OperationId);
            await _cola.RechazarAsync(operacion.OperationId, TipoConflicto.B, motivo);
        }
    }

    public async Task<string?> ObtenerTokenVigenteAsync(CancellationToken ct = default)
    {
        await _candado.WaitAsync(ct);
        bool cambio = false;
        try
        {
            var sesion = await _almacen.LeerAsync<DatosDeSesion>(ClavesDelAlmacen.Sesion);
            if (sesion is null)
            {
                return null;
            }

            if (EsVigente(sesion, Ahora))
            {
                return sesion.AccessToken;
            }

            // Vencido y sin señal: no hay token. Las acciones esperan en la cola; nunca se manda uno vencido.
            if (!_conectividad.HayConexion)
            {
                return null;
            }

            var renovacion = await _cliente.RenovarAsync(sesion.RefreshToken, ct);
            switch (renovacion.Estado)
            {
                case EstadoAutenticacion.Exito:
                    var tokens = renovacion.Tokens!;
                    await _almacen.GuardarAsync(ClavesDelAlmacen.Sesion, sesion with
                    {
                        AccessToken = tokens.AccessToken,
                        RefreshToken = tokens.RefreshToken,
                        AccesoExpiraEnUtc = tokens.ExpiraEnUtc,
                        UltimoContactoUtc = Ahora
                    });
                    cambio = true;
                    return tokens.AccessToken;

                case EstadoAutenticacion.Rechazada:
                case EstadoAutenticacion.OrganizacionSuspendida:
                    // Identity dijo que esta sesión ya no sirve. Se cierra; la cola se conserva para el próximo login.
                    await BorrarTodoAsync();
                    MotivoDeCierre = renovacion.Mensaje;
                    cambio = true;
                    return null;

                default:
                    // Identity no respondió: la sesión sigue, y se reintenta en la próxima sincronización.
                    return null;
            }
        }
        finally
        {
            _candado.Release();
            if (cambio)
            {
                Cambio?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Un servicio respondió 401 con el token que mandó el motor (por ejemplo, Identity cambió su clave). Se marca
    /// el access token como vencido para que la próxima vez se renueve; si la renovación también falla, se cierra.
    /// </summary>
    public async Task NotificarTokenRechazadoAsync(CancellationToken ct = default)
    {
        await _candado.WaitAsync(ct);
        try
        {
            var sesion = await _almacen.LeerAsync<DatosDeSesion>(ClavesDelAlmacen.Sesion);
            if (sesion is not null)
            {
                await _almacen.GuardarAsync(ClavesDelAlmacen.Sesion, sesion with { AccesoExpiraEnUtc = DateTime.MinValue });
            }
        }
        finally
        {
            _candado.Release();
        }
    }

    /// <summary>
    /// Cierra la sesión en este teléfono: revoca el refresh token en Identity si hay señal y borra los tokens y el
    /// perfil. Las acciones en cola no se borran (nunca se pierde una acción): esperan al próximo inicio de sesión.
    /// </summary>
    public async Task CerrarSesionAsync(CancellationToken ct = default)
    {
        await _candado.WaitAsync(ct);
        try
        {
            var sesion = await _almacen.LeerAsync<DatosDeSesion>(ClavesDelAlmacen.Sesion);
            if (sesion is not null && _conectividad.HayConexion)
            {
                await _cliente.CerrarSesionAsync(sesion.RefreshToken, ct);
            }

            await BorrarTodoAsync();
            MotivoDeCierre = null;
        }
        finally
        {
            _candado.Release();
        }

        Cambio?.Invoke(this, EventArgs.Empty);
    }

    private DateTime Ahora => _reloj.GetUtcNow().UtcDateTime;

    /// <summary>"carlos.ruiz@empresa.co" se muestra como "ca***@empresa.co": basta para reconocerla, sin exponerla.</summary>
    private static string Enmascarar(string email)
    {
        var arroba = email.IndexOf('@');
        return arroba <= 0 ? "otra cuenta" : $"{email[..Math.Min(2, arroba)]}***{email[arroba..]}";
    }

    private static bool EsVigente(DatosDeSesion sesion, DateTime ahora) =>
        sesion.AccesoExpiraEnUtc > ahora + MargenDeRenovacion;

    private async Task BorrarTodoAsync()
    {
        foreach (var clave in ClavesDelAlmacen.Todas)
        {
            await _almacen.BorrarAsync(clave);
        }
    }
}
