using EcoTrace.App.Modulos.Identity.Logica;
using EcoTrace.App.Servicios;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Identity;

/// <summary>
/// La pantalla de perfil: el inicio de sesión cuando no hay sesión, y con sesión el perfil en caché, el cambio de
/// datos (encolado) y el cierre de sesión. Toda la lógica está en <see cref="ServicioDePerfil"/> y
/// <see cref="GestorDeSesion"/>; aquí solo se conecta con la vista.
/// </summary>
public sealed class PerfilViewModel : ObservableBase
{
    private readonly GestorDeSesion _sesion;
    private readonly ServicioDePerfil _perfil;
    private readonly IColaLocal _cola;
    private readonly MotorSincronizacion _motor;
    private VistaDePerfil? _vista;
    private bool _conectado;
    private bool _ocupado;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _mensaje = string.Empty;
    private string _nombreEditado = string.Empty;
    private string _telefonoEditado = string.Empty;

    public PerfilViewModel(GestorDeSesion sesion, ServicioDePerfil perfil, IColaLocal cola, MotorSincronizacion motor)
    {
        _sesion = sesion;
        _perfil = perfil;
        _cola = cola;
        _motor = motor;

        IniciarSesion = new Command(async () => await EjecutarAsync(IniciarSesionAsync), () => !Ocupado);
        GuardarCambios = new Command(async () => await EjecutarAsync(GuardarCambiosAsync), () => !Ocupado);
        CerrarOtrasSesiones = new Command(async () => await EjecutarAsync(CerrarOtrasSesionesAsync), () => !Ocupado);
        CerrarSesion = new Command(async () => await EjecutarAsync(CerrarSesionAsync), () => !Ocupado);
        Refrescar = new Command(async () => await EjecutarAsync(RefrescarAsync), () => !Ocupado);
    }

    /// <summary>La página la asigna: pregunta al conductor y devuelve si aceptó.</summary>
    public Func<string, string, Task<bool>> Confirmar { get; set; } = (_, _) => Task.FromResult(true);

    public Command IniciarSesion { get; }

    public Command GuardarCambios { get; }

    public Command CerrarOtrasSesiones { get; }

    public Command CerrarSesion { get; }

    public Command Refrescar { get; }

    public bool SesionIniciada => _vista?.Sesion.Iniciada ?? false;

    public bool SinSesion => !SesionIniciada;

    public bool Ocupado
    {
        get => _ocupado;
        private set
        {
            if (Asignar(ref _ocupado, value))
            {
                foreach (var comando in new[] { IniciarSesion, GuardarCambios, CerrarOtrasSesiones, CerrarSesion, Refrescar })
                {
                    comando.ChangeCanExecute();
                }
            }
        }
    }

    public string Email { get => _email; set => Asignar(ref _email, value); }

    public string Password { get => _password; set => Asignar(ref _password, value); }

    public string Mensaje
    {
        get => _mensaje;
        private set
        {
            if (Asignar(ref _mensaje, value))
            {
                Notificar(nameof(TieneMensaje));
            }
        }
    }

    public bool TieneMensaje => Mensaje.Length > 0;

    public string NombreEditado { get => _nombreEditado; set => Asignar(ref _nombreEditado, value); }

    public string TelefonoEditado { get => _telefonoEditado; set => Asignar(ref _telefonoEditado, value); }

    public string Nombre => _vista?.Nombre ?? "Sin datos guardados todavía";

    public string Telefono => string.IsNullOrEmpty(_vista?.Telefono) ? "Sin teléfono" : _vista.Telefono;

    public string Organizacion =>
        _vista?.Organizacion is null ? "—" : $"{_vista.Organizacion} · {_vista.EstadoOrganizacion}";

    public string Rol => _vista?.Role ?? "—";

    public string Correo => _vista?.Sesion.Email ?? string.Empty;

    public string UltimoLogin => Fecha(_vista?.Sesion.UltimoLoginUtc);

    public string UltimaSincronizacion => Fecha(_vista?.UltimaSincronizacionUtc);

    public string Aviso => _vista?.Aviso ?? string.Empty;

    public bool TieneAviso => Aviso.Length > 0;

    public string Rechazo => _vista?.Rechazo ?? string.Empty;

    public bool TieneRechazo => Rechazo.Length > 0;

    public string ConflictoDelRechazo => Presentacion.Conflicto(_vista?.TipoDeRechazo);

    public string BotonGuardar => _vista?.PuedeCorregir == true ? "Corregir y reintentar" : "Guardar cambios";

    public string MotivoDeCierre => _vista?.Sesion.MotivoDeCierre ?? string.Empty;

    public bool TieneMotivoDeCierre => SinSesion && MotivoDeCierre.Length > 0;

    public void Conectar()
    {
        if (_conectado)
        {
            return;
        }

        _conectado = true;
        _cola.Cambio += AlCambiar;
        _sesion.Cambio += AlCambiar;
    }

    public void Desconectar()
    {
        _conectado = false;
        _cola.Cambio -= AlCambiar;
        _sesion.Cambio -= AlCambiar;
    }

    /// <summary>Lee todo del teléfono (funciona sin señal). Con <paramref name="reiniciarFormulario"/> copia los datos al formulario.</summary>
    public async Task CargarAsync(bool reiniciarFormulario)
    {
        var vista = await _perfil.ObtenerVistaAsync();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _vista = vista;
            if (reiniciarFormulario)
            {
                NombreEditado = vista.Nombre ?? string.Empty;
                TelefonoEditado = vista.Telefono ?? string.Empty;
            }

            Notificar(string.Empty);
        });
    }

    /// <summary>Al abrir la pantalla: muestra lo guardado y, si hay señal, trae el perfil actual en segundo plano.</summary>
    public async Task AlAparecerAsync()
    {
        await CargarAsync(reiniciarFormulario: true);
        if (SesionIniciada && await _perfil.RefrescarAsync())
        {
            await CargarAsync(reiniciarFormulario: true);
        }
    }

    private async Task IniciarSesionAsync()
    {
        var password = Password;
        Password = string.Empty;
        var resultado = await _sesion.IniciarSesionAsync(Email, password);

        if (resultado.Estado == EstadoAutenticacion.AccionesDeOtraCuenta
            && await Confirmar("Acciones de otra cuenta",
                   resultado.Mensaje + "\n\nSi esa cuenta ya no puede entrar, puedes continuar: sus acciones quedan como " +
                   "rechazadas en este teléfono, con el motivo, y no se envían con tu cuenta. ¿Continuar?"))
        {
            resultado = await _sesion.IniciarSesionAsync(Email, password, rechazarAccionesDeOtraCuenta: true);
        }

        if (!resultado.Exitosa)
        {
            Mensaje = resultado.Mensaje;
            return;
        }

        Mensaje = string.Empty;
        await _perfil.RefrescarAsync();
        await CargarAsync(reiniciarFormulario: true);

        // Lo que quedó en cola esperando una sesión sale ya, sin esperar al disparador periódico.
        _ = EnviarYRefrescarAsync();
    }

    private async Task GuardarCambiosAsync()
    {
        try
        {
            if (_vista?.PuedeCorregir == true)
            {
                await _perfil.CorregirAsync(_vista.OperacionRechazada!.Value, NombreEditado, TelefonoEditado);
            }
            else
            {
                await _perfil.CambiarPerfilAsync(NombreEditado, TelefonoEditado);
            }
        }
        catch (ArgumentException ex)
        {
            Mensaje = ex.Message.Split(" (", 2)[0];
            return;
        }

        Mensaje = string.Empty;
        await CargarAsync(reiniciarFormulario: false);
        _ = EnviarYRefrescarAsync();
    }

    private async Task CerrarOtrasSesionesAsync()
    {
        if (!await Confirmar("Cerrar sesión en otros dispositivos",
                "Se cerrará tu sesión en los demás teléfonos cuando esta acción llegue al servidor. Este teléfono sigue con la sesión."))
        {
            return;
        }

        await _perfil.CerrarOtrasSesionesAsync();
        _ = EnviarYRefrescarAsync();
    }

    private async Task CerrarSesionAsync()
    {
        var pendientes = await _cola.ContarAsync(EstadoOperacion.PendienteSync) + await _cola.ContarAsync(EstadoOperacion.Sincronizando);
        var detalle = pendientes == 0
            ? "Tendrás que iniciar sesión con señal para volver a usar la app."
            : $"Tienes {pendientes} {(pendientes == 1 ? "acción" : "acciones")} sin enviar. No se pierden: se envían cuando esta misma cuenta vuelva a iniciar sesión. Mientras tanto, otra cuenta no podrá entrar en este teléfono.";
        if (!await Confirmar("Cerrar sesión", detalle))
        {
            return;
        }

        await _sesion.CerrarSesionAsync();
        await CargarAsync(reiniciarFormulario: true);
    }

    private async Task RefrescarAsync()
    {
        await _motor.SincronizarAsync();
        Mensaje = await _perfil.RefrescarAsync() ? string.Empty : "Sin señal: se muestran los datos guardados.";
        await CargarAsync(reiniciarFormulario: true);
    }

    private async Task EnviarYRefrescarAsync()
    {
        try
        {
            await _motor.SincronizarAsync();
            await _perfil.RefrescarAsync();
            await CargarAsync(reiniciarFormulario: false);
        }
        catch (Exception)
        {
            // Sin señal o con el servidor caído la acción sigue en la cola; el disparador la reintenta.
        }
    }

    private async Task EjecutarAsync(Func<Task> accion)
    {
        Ocupado = true;
        try
        {
            await accion();
        }
        catch (Exception)
        {
            Mensaje = "Algo falló. Lo que estaba en cola sigue guardado.";
        }
        finally
        {
            Ocupado = false;
        }
    }

    private async void AlCambiar(object? remitente, EventArgs e)
    {
        try
        {
            await CargarAsync(reiniciarFormulario: false);
        }
        catch (Exception)
        {
            // Un fallo al refrescar no debe cerrar la app; se reintenta con el próximo cambio.
        }
    }

    private static string Fecha(DateTime? utc) =>
        utc is null ? "Nunca" : utc.Value.ToLocalTime().ToString("dd/MM/yyyy h:mm tt");
}
