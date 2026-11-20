using System.ComponentModel;
using System.Runtime.CompilerServices;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.Modulos.Fleet;

/// <summary>
/// Lo que muestra la pantalla de Fleet: el conductor y su vehículo (de Fleet o de la caché) y el formulario para
/// reportar una novedad. No usa tipos de MAUI, así que se prueba sin emulador; la página solo lo enlaza.
/// </summary>
public sealed class FleetViewModel(ServicioFleet servicio, MotorSincronizacion motor, IConectividad conectividad)
    : INotifyPropertyChanged
{
    /// <summary>Los tipos de novedad como los entiende Fleet, en el mismo orden que <see cref="TiposDeNovedad"/>.</summary>
    private static readonly string[] TiposEnFleet = ["Mecanica", "Llanta", "Accidente", "Otra"];

    private AsignacionEnCache? _asignacion;
    private string _aviso = string.Empty;
    private string _mensaje = string.Empty;
    private string _descripcion = string.Empty;
    private int _tipoSeleccionado;
    private bool _ocupado;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Lo que ve el conductor en el selector. Es List porque el Picker de MAUI pide un IList.</summary>
    public List<string> TiposDeNovedad { get; } = ["Mecánica", "Llanta", "Accidente", "Otra"];

    public int TipoSeleccionado { get => _tipoSeleccionado; set => Asignar(ref _tipoSeleccionado, value); }

    public string Descripcion { get => _descripcion; set => Asignar(ref _descripcion, value ?? string.Empty); }

    /// <summary>Por qué se muestran datos guardados (sin señal, Fleet no respondió...). Vacío si vienen de Fleet.</summary>
    public string Aviso { get => _aviso; private set => Asignar(ref _aviso, value, nameof(TieneAviso)); }

    public bool TieneAviso => Aviso.Length > 0;

    /// <summary>El resultado de la última novedad: guardada, o por qué no se pudo guardar.</summary>
    public string Mensaje { get => _mensaje; private set => Asignar(ref _mensaje, value, nameof(TieneMensaje)); }

    public bool TieneMensaje => Mensaje.Length > 0;

    public bool Ocupado { get => _ocupado; private set => Asignar(ref _ocupado, value, nameof(PuedeReportar)); }

    public bool TieneAsignacion => _asignacion is not null;

    public bool SinAsignacion => _asignacion is null;

    public string NombreConductor => _asignacion?.NombreConductor ?? string.Empty;

    public string DetalleConductor => _asignacion is null ? string.Empty : $"Licencia {_asignacion.Licencia} · {_asignacion.EstadoConductor}";

    public bool TieneVehiculo => _asignacion?.TieneVehiculo == true;

    public bool SinVehiculo => _asignacion is not null && !_asignacion.TieneVehiculo;

    public string Placa => _asignacion?.Placa ?? string.Empty;

    public string DetalleVehiculo => TieneVehiculo
        ? $"{_asignacion!.CapacidadKg} kg · {_asignacion.EstadoVehiculo}"
        : string.Empty;

    public string CargaTexto => _asignacion?.CargaId is { } carga
        ? $"Carga en curso: {carga.ToString()[..8]}"
        : "Sin carga asignada";

    /// <summary>De cuándo son los datos: el conductor tiene que saber si mira algo viejo.</summary>
    public string GuardadoTexto => _asignacion is null
        ? string.Empty
        : $"Datos de Fleet del {_asignacion.GuardadaEnUtc.ToLocalTime():dd/MM HH:mm}";

    /// <summary>Solo se reporta sobre un vehículo conocido, y no mientras se está enviando otra.</summary>
    public bool PuedeReportar => TieneVehiculo && !Ocupado;

    /// <summary>Al abrir la pantalla: primero lo guardado (nunca en blanco) y después lo que diga Fleet.</summary>
    public async Task CargarAsync()
    {
        Mostrar(await servicio.ObtenerDeCacheAsync());
        await ActualizarAsync();
    }

    public async Task ActualizarAsync()
    {
        Ocupado = true;
        try
        {
            Mostrar(await servicio.RefrescarAsync());
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>
    /// Guarda la novedad en la cola. Si hay señal, pide al motor que sincronice ya en vez de esperar su próxima
    /// vuelta; si no, se queda en la cola y sale sola cuando vuelva la conexión.
    /// </summary>
    public async Task ReportarAsync()
    {
        if (TipoSeleccionado < 0 || TipoSeleccionado >= TiposEnFleet.Length)
        {
            Mensaje = "Elige el tipo de novedad.";
            return;
        }

        Ocupado = true;
        try
        {
            var reporte = await servicio.ReportarNovedadAsync(TiposEnFleet[TipoSeleccionado], Descripcion);
            Mensaje = reporte.Mensaje;
            if (!reporte.Encolada)
            {
                return;
            }

            Descripcion = string.Empty;
            if (conectividad.HayConexion)
            {
                await motor.SincronizarAsync();
            }
        }
        finally
        {
            Ocupado = false;
        }
    }

    private void Mostrar(VistaFleet vista)
    {
        _asignacion = vista.Asignacion;
        Aviso = vista.Aviso;

        // Todo lo que se calcula a partir de la asignación cambia junto.
        foreach (var propiedad in new[]
                 {
                     nameof(TieneAsignacion), nameof(SinAsignacion), nameof(NombreConductor), nameof(DetalleConductor),
                     nameof(TieneVehiculo), nameof(SinVehiculo), nameof(Placa), nameof(DetalleVehiculo),
                     nameof(CargaTexto), nameof(GuardadoTexto), nameof(PuedeReportar)
                 })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
        }
    }

    private void Asignar<T>(ref T campo, T valor, string? dependiente = null, [CallerMemberName] string? nombre = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor))
        {
            return;
        }

        campo = valor;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nombre));
        if (dependiente is not null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(dependiente));
        }
    }
}
