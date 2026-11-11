using System.Collections.ObjectModel;
using EcoTrace.App.Servicios;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// El «cerebro» de la pantalla de cargas: conecta lo que ve el conductor con la lógica del módulo. Sigue la regla de oro del
/// ADR 0005, en este orden:
/// <list type="number">
/// <item>Al abrir, pinta de inmediato lo guardado en el teléfono.</item>
/// <item>Después intenta actualizar con el servidor.</item>
/// <item>Cada acción del conductor se guarda en la cola y la pantalla cambia al instante, sin esperar la red.</item>
/// </list>
/// Todo lo que decide algo (qué estado mostrar, qué botones, qué se puede guardar) está en <see cref="ServicioDeCargas"/> y
/// <see cref="AccionesDeCarga"/>, que se prueban sin emulador. Esta clase solo las conecta con la pantalla.
/// </summary>
public sealed class CargoViewModel : ObservableBase
{
    private readonly ServicioDeCargas _servicio;
    private readonly AccionesDeCarga _acciones;
    private readonly IColaLocal _cola;
    private bool _cargando;
    /// <summary>Si no hay ninguna carga que mostrar. Empieza en true hasta que llegue la primera lista.</summary>
    private bool _vacia = true;
    /// <summary>El mensaje actual para el conductor. Vacío cuando no hay nada que avisar.</summary>
    private string _aviso = string.Empty;
    /// <summary>Cuenta cada vez que se pide dibujar la lista, para descartar respuestas lentas que llegan tarde.</summary>
    private int _version;
    /// <summary>Cuántas acciones rechazadas había la última vez que se miró, para actualizar solo cuando aparece una nueva.</summary>
    private int _rechazadasVistas = -1;

    /// <summary>
    /// Prepara la pantalla: crea el botón «Actualizar» y se suscribe a dos avisos: cuando cambia la cola (el conductor actuó o el
    /// motor sincronizó algo) y cuando vuelve la conexión.
    /// </summary>
    public CargoViewModel(
        ServicioDeCargas servicio, AccionesDeCarga acciones, IColaLocal cola, IConectividad conectividad)
    {
        _servicio = servicio;
        _acciones = acciones;
        _cola = cola;

        Actualizar = new Command(async () => await ActualizarAsync(), () => !Cargando);

        // Cuando cambia la cola (el conductor actuó, o el motor sincronizó o rechazó algo) se redibuja desde la copia.
        _cola.Cambio += AlCambiarLaCola;

        // Cuando vuelve la señal se pide la lista al servidor. El motor de sincronización hace lo suyo por aparte.
        conectividad.CambioDeConexion += AlCambiarLaConexion;
    }

    /// <summary>Las cargas que se muestran. La pantalla se redibuja sola cuando esta lista cambia.</summary>
    public ObservableCollection<CargaFila> Cargas { get; } = [];

    /// <summary>El botón «Actualizar»: pide la lista al servidor.</summary>
    public Command Actualizar { get; }

    /// <summary>True mientras se está actualizando con el servidor. Mientras tanto el botón «Actualizar» queda desactivado.</summary>
    public bool Cargando
    {
        get => _cargando;
        private set
        {
            if (Asignar(ref _cargando, value))
            {
                Notificar(nameof(BotonTexto));
                Actualizar.ChangeCanExecute();
            }
        }
    }

    /// <summary>El texto del botón: «Actualizar», o «Actualizando…» mientras trabaja.</summary>
    public string BotonTexto => Cargando ? "Actualizando…" : "Actualizar";

    /// <summary>True si no hay ninguna carga que mostrar. Así se explica en pantalla en vez de dejar un hueco.</summary>
    public bool Vacia
    {
        get => _vacia;
        private set => Asignar(ref _vacia, value);
    }

    /// <summary>El mensaje para el conductor, por ejemplo por qué no se pudo actualizar. Vacío si no hay nada que avisar.</summary>
    public string Aviso
    {
        get => _aviso;
        private set
        {
            if (Asignar(ref _aviso, value))
            {
                Notificar(nameof(TieneAviso));
            }
        }
    }

    /// <summary>True si hay un mensaje que mostrar.</summary>
    public bool TieneAviso => Aviso.Length > 0;

    /// <summary>
    /// Se llama al abrir la pantalla: primero muestra lo guardado en el teléfono y después intenta actualizar con el servidor.
    /// </summary>
    public async Task CargarAsync()
    {
        // Se parte de los rechazos que ya existen, para que solo uno NUEVO dispare una actualización.
        _rechazadasVistas = await _cola.ContarAsync(EstadoOperacion.Rechazado);
        await MostrarAsync(() => _servicio.ObtenerDeCacheAsync());
        await ActualizarAsync();
    }

    /// <summary>Pide la lista al servidor y la muestra. Si falla, deja los datos guardados con un aviso.</summary>
    private async Task ActualizarAsync()
    {
        Cargando = true;
        try
        {
            await MostrarAsync(() => _servicio.RefrescarAsync());
        }
        catch (Exception)
        {
            Aviso = "No se pudo actualizar. Se muestran los datos guardados.";
        }
        finally
        {
            Cargando = false;
        }
    }

    /// <summary>
    /// Pide una vista de las cargas y la dibuja. Si mientras tanto se pidió otra más reciente, descarta esta: así una respuesta
    /// lenta no pisa a una más nueva.
    /// </summary>
    private async Task MostrarAsync(Func<Task<VistaDeCargas>> obtener)
    {
        var version = Interlocked.Increment(ref _version);
        var vista = await obtener();
        if (version != Volatile.Read(ref _version))
        {
            return;
        }

        var filas = vista.Cargas.Select(carga => new CargaFila(carga, ElegirAccionAsync)).ToList();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Cargas.Clear();
            foreach (var fila in filas)
            {
                Cargas.Add(fila);
            }

            Vacia = Cargas.Count == 0;
            Aviso = vista.Aviso;
        });
    }

    /// <summary>
    /// Qué pasa al tocar un botón. La entrega pide confirmación (no se puede deshacer) y la novedad pide la nota. Después la
    /// acción se guarda en la cola, y la pantalla se actualiza sola porque la cola avisa que cambió.
    /// </summary>
    private async Task ElegirAccionAsync(CargaVista carga, string destino)
    {
        try
        {
            string? nota = null;

            if (destino == EstadosDeCarga.ConNovedad)
            {
                nota = await PedirNotaAsync();
                if (nota is null)
                {
                    return;
                }
            }
            else if (destino == EstadosDeCarga.Entregado && !await ConfirmarEntregaAsync(carga))
            {
                return;
            }

            var resultado = await _acciones.CambiarEstadoAsync(carga.CargaId, destino, nota);
            if (!resultado.Exito)
            {
                await AvisarAsync(resultado.Mensaje);
            }
        }
        catch (Exception)
        {
            // Este método corre desde un comando: una excepción aquí cerraría la app.
            await AvisarAsync("No se pudo guardar la acción. Intenta de nuevo.");
        }
    }

    /// <summary>
    /// Se ejecuta cada vez que cambia la cola: vuelve a dibujar la lista desde la copia, sin usar la red. Si apareció un rechazo
    /// nuevo, el estado real de la carga cambió en el servidor, así que actualiza una vez.
    /// </summary>
    private async void AlCambiarLaCola(object? remitente, EventArgs e)
    {
        try
        {
            await MostrarAsync(() => _servicio.ObtenerDeCacheAsync());

            // Si apareció un rechazo nuevo, el estado real de la carga cambió en el servidor: se actualiza una vez.
            // Esto no entra en bucle porque actualizar no crea rechazos, así que la cuenta no vuelve a subir.
            var rechazadas = await _cola.ContarAsync(EstadoOperacion.Rechazado);
            var hayRechazoNuevo = _rechazadasVistas >= 0 && rechazadas > _rechazadasVistas;
            _rechazadasVistas = rechazadas;
            if (hayRechazoNuevo)
            {
                await ActualizarAsync();
            }
        }
        catch (Exception)
        {
            // Un fallo al redibujar no debe tumbar la app; se reintenta con el próximo cambio.
        }
    }

    /// <summary>Se ejecuta cuando cambia la conexión. Si volvió, actualiza la lista con el servidor.</summary>
    private async void AlCambiarLaConexion(object? remitente, bool hayConexion)
    {
        if (!hayConexion)
        {
            return;
        }

        try
        {
            await ActualizarAsync();
        }
        catch (Exception)
        {
            // Se reintenta cuando el conductor abra la pantalla o toque Actualizar.
        }
    }

    /// <summary>La página que está a la vista, donde se muestran los cuadros de diálogo.</summary>
    private static Page? Pagina => Application.Current?.MainPage;

    /// <summary>Muestra un cuadro para escribir la nota de una novedad. Devuelve null si el conductor cancela.</summary>
    private static async Task<string?> PedirNotaAsync() =>
        Pagina is { } pagina
            ? await MainThread.InvokeOnMainThreadAsync(() => pagina.DisplayPromptAsync(
                "Reportar novedad", "¿Qué ocurrió? Esto le llega al despacho.", "Guardar", "Cancelar",
                maxLength: AccionesDeCarga.NotaMaxima, keyboard: Keyboard.Text))
            : null;

    /// <summary>Pregunta «¿entregaste esta carga?» antes de confirmar la entrega, porque no se puede deshacer.</summary>
    private static async Task<bool> ConfirmarEntregaAsync(CargaVista carga) =>
        Pagina is { } pagina
        && await MainThread.InvokeOnMainThreadAsync(() => pagina.DisplayAlert(
            "Confirmar entrega", $"¿Entregaste «{carga.Descripcion}» en {carga.Destino}? Esto no se puede deshacer.", "Sí, entregada", "Todavía no"));

    /// <summary>Muestra un mensaje al conductor en un cuadro de diálogo.</summary>
    private static async Task AvisarAsync(string mensaje)
    {
        if (Pagina is { } pagina)
        {
            await MainThread.InvokeOnMainThreadAsync(() => pagina.DisplayAlert("Cargas", mensaje, "Entendido"));
        }
    }
}
