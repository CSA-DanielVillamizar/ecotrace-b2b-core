using System.Collections.ObjectModel;
using EcoTrace.App.Servicios;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Cargo;

/// <summary>
/// La pantalla de cargas. Sigue la regla de oro del ADR 0005, en este orden:
/// (1) al abrir, pinta de inmediato lo guardado en el teléfono; (2) después intenta actualizar con el servidor;
/// (3) cada acción del conductor se guarda en la cola y la pantalla cambia al instante, sin esperar la red.
/// <para>
/// Todo lo que decide algo (qué estado mostrar, qué botones, qué se puede encolar) está en
/// <see cref="ServicioDeCargas"/> y <see cref="AccionesDeCarga"/>, que se prueban sin emulador. Esta clase solo conecta
/// esas piezas con la pantalla.
/// </para>
/// </summary>
public sealed class CargoViewModel : ObservableBase
{
    private readonly ServicioDeCargas _servicio;
    private readonly AccionesDeCarga _acciones;
    private readonly IColaLocal _cola;
    private bool _cargando;
    private bool _vacia = true;
    private string _aviso = string.Empty;
    private int _version;
    private int _rechazadasVistas = -1;

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

    public ObservableCollection<CargaFila> Cargas { get; } = [];

    public Command Actualizar { get; }

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

    public string BotonTexto => Cargando ? "Actualizando…" : "Actualizar";

    /// <summary>True si no hay ninguna carga que mostrar (se explica en pantalla en vez de dejar un hueco).</summary>
    public bool Vacia
    {
        get => _vacia;
        private set => Asignar(ref _vacia, value);
    }

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

    public bool TieneAviso => Aviso.Length > 0;

    /// <summary>Se llama al abrir la pantalla: primero lo guardado, después el servidor.</summary>
    public async Task CargarAsync()
    {
        // Se parte de los rechazos que ya existen, para que solo uno NUEVO dispare una actualización.
        _rechazadasVistas = await _cola.ContarAsync(EstadoOperacion.Rechazado);
        await MostrarAsync(() => _servicio.ObtenerDeCacheAsync());
        await ActualizarAsync();
    }

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
    /// Pide una vista y la dibuja. Si mientras tanto se pidió otra más reciente, descarta esta: así una respuesta lenta
    /// no pisa a una más nueva.
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
    /// Qué pasa al tocar un botón. La entrega pide confirmación (no se puede deshacer) y la novedad pide la nota.
    /// Después la acción se guarda en la cola: la pantalla se actualiza sola porque la cola avisa que cambió.
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

    private static Page? Pagina => Application.Current?.MainPage;

    private static async Task<string?> PedirNotaAsync() =>
        Pagina is { } pagina
            ? await MainThread.InvokeOnMainThreadAsync(() => pagina.DisplayPromptAsync(
                "Reportar novedad", "¿Qué ocurrió? Esto le llega al despacho.", "Guardar", "Cancelar",
                maxLength: AccionesDeCarga.NotaMaxima, keyboard: Keyboard.Text))
            : null;

    private static async Task<bool> ConfirmarEntregaAsync(CargaVista carga) =>
        Pagina is { } pagina
        && await MainThread.InvokeOnMainThreadAsync(() => pagina.DisplayAlert(
            "Confirmar entrega", $"¿Entregaste «{carga.Descripcion}» en {carga.Destino}? Esto no se puede deshacer.", "Sí, entregada", "Todavía no"));

    private static async Task AvisarAsync(string mensaje)
    {
        if (Pagina is { } pagina)
        {
            await MainThread.InvokeOnMainThreadAsync(() => pagina.DisplayAlert("Cargas", mensaje, "Entendido"));
        }
    }
}
