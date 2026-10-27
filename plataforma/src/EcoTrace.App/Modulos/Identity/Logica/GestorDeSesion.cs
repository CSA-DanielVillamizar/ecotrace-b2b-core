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
    string? MotivoDeCierre);

/// <summary>
/// La sesión del conductor (ADR 0004 y 0005). Es el <see cref="IProveedorDeSesion"/> que usa el motor de
/// sincronización de los cuatro módulos: les da el access token vigente, lo renueva con el refresh token cuando
/// vence y <b>nunca</b> devuelve uno vencido. Los tokens viven solo en el almacén seguro.
/// </summary>
public sealed class GestorDeSesion : IProveedorDeSesion
{
    /// <summary>Se renueva un poco antes de que venza, para que no expire en medio de un envío.</summary>
    public static readonly TimeSpan MargenDeRenovacion = TimeSpan.FromSeconds(60);

    private readonly IAlmacenSeguro _almacen;
    private readonly ClienteIdentity _cliente;
    private readonly IConectividad _conectividad;
    private readonly IColaLocal _cola;
    private readonly TimeProvider _reloj;
    private readonly SemaphoreSlim _candado = new(1, 1);

    public GestorDeSesion(
        IAlmacenSeguro almacen, ClienteIdentity cliente, IConectividad conectividad, IColaLocal cola, TimeProvider? reloj = null)
    {
        _almacen = almacen;
        _cliente = cliente;
        _conectividad = conectividad;
        _cola = cola;
        _reloj = reloj ?? TimeProvider.System;
    }

    /// <summary>Cambió la sesión: se inició, se renovó o se cerró.</summary>
    public event EventHandler? Cambio;

    /// <summary>Por qué se cerró la sesión sin que el conductor lo pidiera (venció, organización suspendida...).</summary>
    public string? MotivoDeCierre { get; private set; }

    public async Task<EstadoDeSesion> EstadoAsync()
    {
        var sesion = await _almacen.LeerAsync<DatosDeSesion>(ClavesDelAlmacen.Sesion);
        return sesion is null
            ? new EstadoDeSesion(false, null, null, null, false, MotivoDeCierre)
            : new EstadoDeSesion(true, sesion.Email, sesion.UltimoLoginUtc, sesion.UltimoContactoUtc,
                EsVigente(sesion, Ahora), MotivoDeCierre);
    }

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
