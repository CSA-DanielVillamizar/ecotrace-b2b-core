using System.Collections.ObjectModel;
using EcoTrace.App.Servicios;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Conexion;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.ViewModels;

/// <summary>Una acción de la cola tal como se le muestra al conductor.</summary>
public sealed class OperacionItem
{
    public OperacionItem(OperacionPendiente operacion, DateTime ahoraUtc)
    {
        Titulo = Presentacion.Accion(operacion.Tipo);
        Detalle = $"{operacion.Modulo} · {Hace(operacion.CreadaEnUtc, ahoraUtc)}";
        EstadoTexto = Presentacion.Estado(operacion.Estado);
        Conflicto = Presentacion.Conflicto(operacion.TipoConflicto);

        // Una acción en cola que ya falló una vez dice por qué: el conductor no se queda sin saber qué pasa.
        Motivo = operacion.Estado == EstadoOperacion.Rechazado || (operacion.Estado == EstadoOperacion.PendienteSync && operacion.Intentos > 0)
            ? operacion.MotivoRechazo ?? string.Empty
            : string.Empty;

        (EstadoFondo, EstadoColor) = operacion.Estado switch
        {
            EstadoOperacion.PendienteSync => (Paleta.InfoFondo, Paleta.InfoTexto),
            EstadoOperacion.Sincronizando => (Paleta.AdvertenciaFondo, Paleta.AdvertenciaTexto),
            EstadoOperacion.Sincronizado => (Paleta.CorrectoFondo, Paleta.CorrectoTexto),
            _ => (Paleta.ErrorFondo, Paleta.ErrorTexto)
        };
    }

    public string Titulo { get; }

    public string Detalle { get; }

    public string EstadoTexto { get; }

    public Color EstadoFondo { get; }

    public Color EstadoColor { get; }

    public string Conflicto { get; }

    public bool TieneConflicto => Conflicto.Length > 0;

    public string Motivo { get; }

    public bool TieneMotivo => Motivo.Length > 0;

    private static string Hace(DateTime creadaUtc, DateTime ahoraUtc)
    {
        var pasado = ahoraUtc - creadaUtc;
        return pasado.TotalMinutes < 1 ? "ahora"
            : pasado.TotalHours < 1 ? $"hace {(int)pasado.TotalMinutes} min"
            : pasado.TotalDays < 1 ? $"hace {(int)pasado.TotalHours} h"
            : creadaUtc.ToLocalTime().ToString("dd/MM HH:mm");
    }
}

/// <summary>La lista de acciones de la cola, de todos los módulos o de uno solo. Se refresca cuando cambia la cola.</summary>
public sealed class ListaOperacionesViewModel : ObservableBase
{
    private readonly IColaLocal _cola;
    private readonly string? _modulo;
    private readonly Action<Action> _enHiloPrincipal;
    private bool _vacia = true;

    public ListaOperacionesViewModel(IColaLocal cola, string? modulo, Action<Action> enHiloPrincipal)
    {
        _cola = cola;
        _modulo = modulo;
        _enHiloPrincipal = enHiloPrincipal;
    }

    public ObservableCollection<OperacionItem> Items { get; } = [];

    public bool Vacia
    {
        get => _vacia;
        private set => Asignar(ref _vacia, value);
    }

    public void Conectar() => _cola.Cambio += AlCambiar;

    public void Desconectar() => _cola.Cambio -= AlCambiar;

    public async Task CargarAsync()
    {
        var operaciones = await _cola.ListarAsync(modulo: _modulo);
        var ahora = DateTime.UtcNow;

        // Lo más reciente arriba, y no más de 50: es un resumen, no un historial.
        var items = operaciones.OrderByDescending(o => o.Secuencia).Take(50).Select(o => new OperacionItem(o, ahora)).ToList();
        _enHiloPrincipal(() =>
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            Vacia = Items.Count == 0;
        });
    }

    private async void AlCambiar(object? remitente, EventArgs e)
    {
        try
        {
            await CargarAsync();
        }
        catch (Exception)
        {
            // Un fallo al refrescar la lista no debe tumbar la app; se reintenta con el próximo cambio.
        }
    }
}

public sealed class InicioViewModel : ObservableBase
{
    private readonly IColaLocal _cola;
    private readonly MotorSincronizacion _motor;
    private readonly IConectividad _conectividad;
    private readonly ConfiguracionServicios _configuracion;
    private int _enCola;
    private int _enviadas;
    private int _rechazadas;
    private bool _sincronizando;
    private bool _enLinea;
    private string _mensaje = string.Empty;

    public InicioViewModel(
        IColaLocal cola, MotorSincronizacion motor, IConectividad conectividad, ConfiguracionServicios configuracion)
    {
        _cola = cola;
        _motor = motor;
        _conectividad = conectividad;
        _configuracion = configuracion;
        _enLinea = conectividad.HayConexion;

        Sincronizar = new Command(async () => await SincronizarAsync(), () => !Sincronizando);
        _conectividad.CambioDeConexion += (_, hay) => MainThread.BeginInvokeOnMainThread(() => EnLinea = hay);
        _cola.Cambio += async (_, _) =>
        {
            try
            {
                await ActualizarAsync();
            }
            catch (Exception)
            {
                // Se reintenta con el próximo cambio.
            }
        };
    }

    public Command Sincronizar { get; }

    public int EnCola { get => _enCola; private set => Asignar(ref _enCola, value); }

    public int Enviadas { get => _enviadas; private set => Asignar(ref _enviadas, value); }

    public int Rechazadas { get => _rechazadas; private set => Asignar(ref _rechazadas, value); }

    public string Mensaje { get => _mensaje; private set { if (Asignar(ref _mensaje, value)) Notificar(nameof(TieneMensaje)); } }

    public bool TieneMensaje => Mensaje.Length > 0;

    public bool EnLinea
    {
        get => _enLinea;
        private set
        {
            if (Asignar(ref _enLinea, value))
            {
                Notificar(nameof(ConexionTexto));
                Notificar(nameof(ConexionFondo));
                Notificar(nameof(ConexionColor));
            }
        }
    }

    public bool Sincronizando
    {
        get => _sincronizando;
        private set
        {
            if (Asignar(ref _sincronizando, value))
            {
                Notificar(nameof(BotonTexto));
                Sincronizar.ChangeCanExecute();
            }
        }
    }

    public string BotonTexto => Sincronizando ? "Sincronizando…" : "Sincronizar ahora";

    public string ConexionTexto => EnLinea ? "En línea" : "Sin conexión";

    public Color ConexionFondo => EnLinea ? Paleta.CorrectoFondo : Paleta.AdvertenciaFondo;

    public Color ConexionColor => EnLinea ? Paleta.CorrectoTexto : Paleta.AdvertenciaTexto;

    public string EntornoTexto => _configuracion.EsProduccion ? "Entorno: producción" : "Entorno: desarrollo";

    public async Task ActualizarAsync()
    {
        var enCola = await _cola.ContarAsync(EstadoOperacion.PendienteSync) + await _cola.ContarAsync(EstadoOperacion.Sincronizando);
        var enviadas = await _cola.ContarAsync(EstadoOperacion.Sincronizado);
        var rechazadas = await _cola.ContarAsync(EstadoOperacion.Rechazado);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            EnCola = enCola;
            Enviadas = enviadas;
            Rechazadas = rechazadas;
            EnLinea = _conectividad.HayConexion;
        });
    }

    private async Task SincronizarAsync()
    {
        Sincronizando = true;
        try
        {
            Mensaje = Presentacion.Resultado(await _motor.SincronizarAsync());
        }
        catch (Exception)
        {
            Mensaje = "No se pudo sincronizar. La acción sigue en cola.";
        }
        finally
        {
            Sincronizando = false;
            await ActualizarAsync();
        }
    }
}
