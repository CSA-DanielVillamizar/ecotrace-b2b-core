using System.Collections.ObjectModel;
using System.Text.Json;
using EcoTrace.App.Servicios;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sesion;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.App.ViewModels;

// Los datos tal como los devuelven los servicios. Los enums llegan como texto.
public sealed record CargaDto(Guid CargaId, string Descripcion, string Origen, string Destino, int PesoKg, string Estado);

public sealed record AsignacionDto(Guid VehiculoId, Guid ConductorId);

public sealed record CargaDetalleDto(CargaDto Carga, AsignacionDto? Asignacion);

public sealed record VehiculoDto(Guid VehiculoId, string Placa, int CapacidadKg, string Estado, Guid? ReservadoParaCargaId);

public sealed record PagoDto(Guid PagoId, Guid CargaId, decimal Monto, string Moneda, string EstadoEscrow);

/// <summary>
/// Lo que comparten las pantallas de los módulos: leer del servidor con caché, decir de cuándo son los datos y encolar una acción
/// respetando las reglas de la sesión. La acción se guarda en la cola local <b>antes</b> de tocar la red y se envía después.
/// </summary>
public abstract class ModuloViewModelBase : ObservableBase
{
    private string _aviso = string.Empty;
    private string _mensaje = string.Empty;
    private string _actualizado = string.Empty;
    private bool _cargando;

    protected ModuloViewModelBase(
        ServicioDeSesion sesion, IColaLocal cola, LectorConCache lector, ConfiguracionServicios servicios, RegistroDeModulos registro)
    {
        Sesion = sesion;
        Cola = cola;
        Lector = lector;
        Servicios = servicios;
        Registro = registro;
        Refrescar = new Command(async () => await AlAparecerAsync(), () => !Cargando);
    }

    protected ServicioDeSesion Sesion { get; }

    protected IColaLocal Cola { get; }

    protected LectorConCache Lector { get; }

    protected ConfiguracionServicios Servicios { get; }

    protected RegistroDeModulos Registro { get; }

    public Command Refrescar { get; }

    /// <summary>Por qué se muestran datos guardados (sin conexión, el servidor no respondió…).</summary>
    public string Aviso { get => _aviso; protected set { if (Asignar(ref _aviso, value)) Notificar(nameof(TieneAviso)); } }

    public bool TieneAviso => Aviso.Length > 0;

    /// <summary>Lo que pasó con la última acción: guardada en la cola, o por qué no se pudo.</summary>
    public string Mensaje { get => _mensaje; protected set { if (Asignar(ref _mensaje, value)) Notificar(nameof(TieneMensaje)); } }

    public bool TieneMensaje => Mensaje.Length > 0;

    /// <summary>Cuándo son los datos que se ven, y si vienen del servidor o de lo guardado.</summary>
    public string Actualizado { get => _actualizado; protected set => Asignar(ref _actualizado, value); }

    public bool Cargando
    {
        get => _cargando;
        protected set
        {
            if (Asignar(ref _cargando, value))
            {
                Refrescar.ChangeCanExecute();
            }
        }
    }

    public async Task AlAparecerAsync()
    {
        Cargando = true;
        try
        {
            if (await Sesion.ModoAsync() == ModoDeSesion.SinSesion)
            {
                Aviso = "Inicia sesión en la pestaña Perfil para ver tus datos.";
                Actualizado = string.Empty;
                await LimpiarAsync();
                return;
            }

            await CargarAsync();
        }
        catch (Exception)
        {
            Aviso = "No se pudieron cargar los datos. Se reintentará al refrescar.";
        }
        finally
        {
            Cargando = false;
        }
    }

    protected abstract Task CargarAsync();

    protected abstract Task LimpiarAsync();

    protected void MostrarOrigen<T>(DatosConOrigen<T> datos)
    {
        Aviso = datos.Aviso ?? string.Empty;
        Actualizado = datos.ActualizadosUtc is { } cuando ? TextosDeDatos.Actualizacion(datos.DelServidor, cuando, DateTime.UtcNow, TimeZoneInfo.Local) : string.Empty;

        // Con datos frescos del servidor hay conexión y sesión: lo que se dijo antes («con la sesión vencida no se puede…»,
        // «Guardada. Se enviará…») ya no es cierto y no debe quedarse en pantalla.
        if (datos.DelServidor)
        {
            Mensaje = string.Empty;
        }
    }


    /// <summary>Guarda la acción en la cola local si las reglas de la sesión lo permiten. Devuelve si se guardó.</summary>
    protected async Task<bool> EncolarAsync(string modulo, string tipo, object datos)
    {
        var critica = Registro.EsCritica(modulo, tipo);
        if (!await Sesion.PuedeEncolarAsync(critica))
        {
            var modo = await Sesion.ModoAsync();
            Mensaje = modo == ModoDeSesion.Degradada && !critica
                ? "Abre la pestaña Perfil y desbloquea con tu huella o PIN para seguir sin conexión."
                : PoliticaDeSesion.Explicar(modo, critica) is { Length: > 0 } explicacion ? explicacion : "No se puede guardar esta acción ahora.";
            return false;
        }

        await Cola.EncolarAsync(modulo, tipo, JsonSerializer.Serialize(datos));
        Mensaje = "Guardada. Se enviará cuando haya conexión.";
        return true;
    }
}

public sealed class CargaItem
{
    public CargaItem(CargaDto carga, Guid? conductorId, Func<CargaItem, Task> confirmar)
    {
        Carga = carga;
        ConductorId = conductorId;
        Confirmar = new Command(async () => await confirmar(this), () => PuedeEntregar);
    }

    public CargaDto Carga { get; }

    /// <summary>Conductor asignado según el último dato guardado. Viaja con la acción: si la carga se reasignó, el servidor lo detecta.</summary>
    public Guid? ConductorId { get; }

    public string Titulo => Carga.Descripcion;

    public string Ruta => $"{Carga.Origen} → {Carga.Destino}";

    public string Detalle => $"{Carga.PesoKg:N0} kg";

    public string EstadoTexto => Carga.Estado switch
    {
        "Asignado" => "Asignada",
        "EnTransito" => "En tránsito",
        "ConNovedad" => "Con novedad",
        "Entregado" => "Entregada",
        _ => "Pendiente"
    };

    public bool PuedeEntregar => Carga.Estado is "EnTransito" or "ConNovedad";

    public Command Confirmar { get; }
}

public sealed class CargoViewModel : ModuloViewModelBase
{
    public CargoViewModel(
        ServicioDeSesion sesion, IColaLocal cola, LectorConCache lector, ConfiguracionServicios servicios, RegistroDeModulos registro)
        : base(sesion, cola, lector, servicios, registro)
    {
    }

    public ObservableCollection<CargaItem> Cargas { get; } = [];

    public bool Vacia => Cargas.Count == 0;

    /// <summary>Las cargas guardadas, para que otros módulos (Fleet) ofrezcan sobre cuál reportar una novedad.</summary>
    public const string ClaveDeLista = "cargo.cargas";

    protected override async Task CargarAsync()
    {
        var lista = await Lector.LeerAsync<List<CargaDto>>(ClaveDeLista, new Uri(Servicios.Cargo, "/api/cargas"));
        MostrarOrigen(lista);

        var items = new List<CargaItem>();
        foreach (var carga in lista.Datos ?? [])
        {
            // El conductor asignado viaja en el detalle de cada carga que todavía está en marcha.
            Guid? conductor = null;
            if (carga.Estado is "Asignado" or "EnTransito" or "ConNovedad")
            {
                var detalle = await Lector.LeerAsync<CargaDetalleDto>($"cargo.detalle.{carga.CargaId}", new Uri(Servicios.Cargo, $"/api/cargas/{carga.CargaId}"));
                conductor = detalle.Datos?.Asignacion?.ConductorId;
            }

            items.Add(new CargaItem(carga, conductor, ConfirmarAsync));
        }

        Reemplazar(items);
    }

    protected override Task LimpiarAsync()
    {
        Reemplazar([]);
        return Task.CompletedTask;
    }

    private void Reemplazar(List<CargaItem> items) => MainThread.BeginInvokeOnMainThread(() =>
    {
        Cargas.Clear();
        foreach (var item in items)
        {
            Cargas.Add(item);
        }

        Notificar(nameof(Vacia));
    });

    private async Task ConfirmarAsync(CargaItem item)
    {
        await EncolarAsync("Cargo", "ConfirmarEntrega", new
        {
            cargaId = item.Carga.CargaId, conductorId = item.ConductorId, ubicacion = item.Carga.Destino
        });
    }
}

public sealed class FleetViewModel : ModuloViewModelBase
{
    private VehiculoDto? _vehiculo;
    private CargaDto? _carga;
    private string _descripcion = string.Empty;

    public FleetViewModel(
        ServicioDeSesion sesion, IColaLocal cola, LectorConCache lector, ConfiguracionServicios servicios, RegistroDeModulos registro)
        : base(sesion, cola, lector, servicios, registro)
    {
        Reportar = new Command(async () => await ReportarAsync());
    }

    public ObservableCollection<VehiculoDto> Vehiculos { get; } = [];

    public ObservableCollection<CargaDto> Cargas { get; } = [];

    public VehiculoDto? Vehiculo { get => _vehiculo; set => Asignar(ref _vehiculo, value); }

    public CargaDto? Carga { get => _carga; set => Asignar(ref _carga, value); }

    public string Descripcion { get => _descripcion; set => Asignar(ref _descripcion, value); }

    public Command Reportar { get; }

    protected override async Task CargarAsync()
    {
        var vehiculos = await Lector.LeerAsync<List<VehiculoDto>>("fleet.vehiculos", new Uri(Servicios.Fleet, "/api/vehiculos"));
        MostrarOrigen(vehiculos);

        // Las cargas salen del caché del módulo Cargo: la novedad se reporta sobre la carga que el vehículo llevaba.
        var cargas = await Lector.LeerAsync<List<CargaDto>>(CargoViewModel.ClaveDeLista, new Uri(Servicios.Cargo, "/api/cargas"));

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Reemplazar(Vehiculos, vehiculos.Datos ?? []);
            Reemplazar(Cargas, (cargas.Datos ?? []).Where(c => c.Estado != "Entregado"));
            Vehiculo ??= Vehiculos.FirstOrDefault();
            Carga ??= Cargas.FirstOrDefault();
        });
    }

    protected override Task LimpiarAsync()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Vehiculos.Clear();
            Cargas.Clear();
        });
        return Task.CompletedTask;
    }

    private static void Reemplazar<T>(ObservableCollection<T> destino, IEnumerable<T> origen)
    {
        destino.Clear();
        foreach (var elemento in origen)
        {
            destino.Add(elemento);
        }
    }

    private async Task ReportarAsync()
    {
        if (Vehiculo is null || Carga is null)
        {
            Mensaje = "Elige el vehículo y la carga que llevaba.";
            return;
        }

        if (Descripcion.Trim().Length < 3)
        {
            Mensaje = "Cuenta qué pasó, en al menos 3 caracteres.";
            return;
        }

        if (await EncolarAsync("Fleet", "ReportarNovedad", new { vehiculoId = Vehiculo.VehiculoId, cargaId = Carga.CargaId, descripcion = Descripcion.Trim() }))
        {
            Descripcion = string.Empty;
        }
    }
}

public sealed class PagoItem
{
    public PagoItem(PagoDto pago, Func<PagoItem, Task> registrar)
    {
        Pago = pago;
        RegistrarEvidencia = new Command(async () => await registrar(this));
    }

    public PagoDto Pago { get; }

    public string Titulo => $"$ {Pago.Monto:N0} {Pago.Moneda}";

    public string EstadoTexto => Pago.EstadoEscrow switch
    {
        "EnCustodia" => "En custodia",
        "Liberado" => "Liberado",
        "Reembolsado" => "Reembolsado",
        "EnDisputa" => "En disputa",
        _ => Pago.EstadoEscrow
    };

    public Color EstadoFondo => Pago.EstadoEscrow switch
    {
        "Liberado" => Paleta.CorrectoFondo,
        "Reembolsado" or "EnDisputa" => Paleta.AdvertenciaFondo,
        _ => Paleta.InfoFondo
    };

    public Color EstadoColor => Pago.EstadoEscrow switch
    {
        "Liberado" => Paleta.CorrectoTexto,
        "Reembolsado" or "EnDisputa" => Paleta.AdvertenciaTexto,
        _ => Paleta.InfoTexto
    };

    public bool PuedeRegistrarEvidencia => Pago.EstadoEscrow == "EnCustodia";

    public Command RegistrarEvidencia { get; }
}

public sealed class BillingViewModel : ModuloViewModelBase
{
    private string _descripcion = string.Empty;

    public BillingViewModel(
        ServicioDeSesion sesion, IColaLocal cola, LectorConCache lector, ConfiguracionServicios servicios, RegistroDeModulos registro)
        : base(sesion, cola, lector, servicios, registro)
    {
    }

    public ObservableCollection<PagoItem> Pagos { get; } = [];

    public bool Vacia => Pagos.Count == 0;

    /// <summary>Lo que se anota como evidencia de entrega (qué se entregó y a quién).</summary>
    public string Descripcion { get => _descripcion; set => Asignar(ref _descripcion, value); }

    protected override async Task CargarAsync()
    {
        var pagos = await Lector.LeerAsync<List<PagoDto>>("billing.pagos", new Uri(Servicios.Billing, "/api/pagos"));
        MostrarOrigen(pagos);
        Reemplazar((pagos.Datos ?? []).Select(p => new PagoItem(p, RegistrarAsync)).ToList());
    }

    protected override Task LimpiarAsync()
    {
        Reemplazar([]);
        return Task.CompletedTask;
    }

    private void Reemplazar(List<PagoItem> items) => MainThread.BeginInvokeOnMainThread(() =>
    {
        Pagos.Clear();
        foreach (var item in items)
        {
            Pagos.Add(item);
        }

        Notificar(nameof(Vacia));
    });

    private async Task RegistrarAsync(PagoItem item)
    {
        if (Descripcion.Trim().Length < 3)
        {
            Mensaje = "Escribe arriba qué evidencia registras (al menos 3 caracteres).";
            return;
        }

        await EncolarAsync("Billing", "RegistrarEvidencia", new { pagoId = item.Pago.PagoId, descripcion = Descripcion.Trim(), referencia = (string?)null });
    }
}

public sealed class PerfilViewModel : ObservableBase
{
    private readonly ServicioDeSesion _sesion;
    private readonly IColaLocal _cola;
    private readonly IConectividad _conectividad;
    private string _correo = string.Empty;
    private string _clave = string.Empty;
    private string _error = string.Empty;
    private string _telefono = string.Empty;
    private string _mensaje = string.Empty;
    private ModoDeSesion _modo;
    private DatosDeSesion? _datos;
    private bool _trabajando;
    private Guid? _corrigiendo;

    public PerfilViewModel(ServicioDeSesion sesion, IColaLocal cola, IConectividad conectividad)
    {
        _sesion = sesion;
        _cola = cola;
        _conectividad = conectividad;
        IniciarSesion = new Command(async () => await IniciarAsync(), () => !Trabajando);
        CerrarSesion = new Command(async () => { await _sesion.CerrarSesionAsync(); await ActualizarAsync(); });
        Desbloquear = new Command(async () => { await _sesion.DesbloquearSesionDegradadaAsync(); await ActualizarAsync(); });
        GuardarTelefono = new Command(async () => await GuardarTelefonoAsync());
        _sesion.Cambio += async (_, _) => await ActualizarSeguroAsync();
        _conectividad.CambioDeConexion += async (_, _) => await ActualizarSeguroAsync();
    }

    public Command IniciarSesion { get; }

    public Command CerrarSesion { get; }

    public Command Desbloquear { get; }

    public Command GuardarTelefono { get; }

    public string Correo { get => _correo; set => Asignar(ref _correo, value); }

    public string Clave { get => _clave; set => Asignar(ref _clave, value); }

    public string Telefono { get => _telefono; set => Asignar(ref _telefono, value); }

    public string Error { get => _error; private set { if (Asignar(ref _error, value)) Notificar(nameof(TieneError)); } }

    public bool TieneError => Error.Length > 0;

    public string Mensaje { get => _mensaje; private set { if (Asignar(ref _mensaje, value)) Notificar(nameof(TieneMensaje)); } }

    public bool TieneMensaje => Mensaje.Length > 0;

    public bool Trabajando
    {
        get => _trabajando;
        private set
        {
            if (Asignar(ref _trabajando, value))
            {
                IniciarSesion.ChangeCanExecute();
            }
        }
    }

    public bool ConSesion => _modo != ModoDeSesion.SinSesion;

    public bool SinSesion => !ConSesion;

    public bool EsDegradada => _modo == ModoDeSesion.Degradada;

    public string Quien => _datos is null ? string.Empty : $"{_datos.Email} · {_datos.Rol}";

    public string ModoTexto => _modo switch
    {
        ModoDeSesion.Normal => "Sesión activa",
        ModoDeSesion.Degradada => "Sesión sin conexión · limitada",
        ModoDeSesion.Expirada => "La sesión terminó",
        _ => string.Empty
    };

    public string ModoAyuda => _modo switch
    {
        ModoDeSesion.Degradada =>
            $"Sin conexión y con el token vencido. Puedes trabajar hasta 48 horas desde tu último contacto con el servidor, sin confirmar entregas ni mover dinero. Último contacto: {_datos?.UltimoContactoUtc.ToLocalTime():dd/MM HH:mm}.",
        ModoDeSesion.Expirada => "Conéctate a internet y vuelve a iniciar sesión para seguir enviando tus acciones.",
        _ => string.Empty
    };

    public Color ModoFondo => _modo switch
    {
        ModoDeSesion.Normal => Paleta.CorrectoFondo,
        ModoDeSesion.Degradada => Paleta.AdvertenciaFondo,
        _ => Paleta.ErrorFondo
    };

    public Color ModoColor => _modo switch
    {
        ModoDeSesion.Normal => Paleta.CorrectoTexto,
        ModoDeSesion.Degradada => Paleta.AdvertenciaTexto,
        _ => Paleta.ErrorTexto
    };

    public async Task ActualizarAsync()
    {
        _datos = await _sesion.ActualAsync();
        _modo = await _sesion.ModoAsync();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var propiedad in new[]
            {
                nameof(ConSesion), nameof(SinSesion), nameof(EsDegradada), nameof(Quien), nameof(ModoTexto),
                nameof(ModoAyuda), nameof(ModoFondo), nameof(ModoColor)
            })
            {
                Notificar(propiedad);
            }
        });
    }

    private async Task ActualizarSeguroAsync()
    {
        try
        {
            await ActualizarAsync();
        }
        catch (Exception)
        {
            // Se vuelve a leer con el próximo cambio.
        }
    }

    private async Task IniciarAsync()
    {
        Error = string.Empty;
        if (string.IsNullOrWhiteSpace(Correo) || string.IsNullOrEmpty(Clave))
        {
            Error = "Escribe tu correo y tu contraseña.";
            return;
        }

        Trabajando = true;
        try
        {
            var resultado = await _sesion.IniciarSesionAsync(Correo, Clave);
            if (resultado.Exito)
            {
                // La contraseña no se guarda en ninguna parte: se borra de la pantalla en cuanto se usó.
                Clave = string.Empty;
                Mensaje = string.Empty;
            }
            else
            {
                Error = resultado.Mensaje;
            }
        }
        finally
        {
            Trabajando = false;
            await ActualizarAsync();
        }
    }

    private async Task GuardarTelefonoAsync()
    {
        Mensaje = string.Empty;
        if (_datos is null)
        {
            return;
        }

        if (!await _sesion.PuedeEncolarAsync(accionCritica: false))
        {
            Mensaje = _modo == ModoDeSesion.Degradada
                ? "Desbloquea con tu huella o PIN para seguir sin conexión."
                : PoliticaDeSesion.Explicar(_modo, false);
            return;
        }

        var payload = JsonSerializer.Serialize(new { userId = _datos.UserId, telefono = Telefono.Trim() });
        if (_corrigiendo is { } rechazada)
        {
            // Corregir y reintentar: una acción nueva con el dato corregido. La rechazada se conserva en la cola, a la vista.
            _corrigiendo = null;
            await _cola.ReintentarCorregidaAsync(rechazada, payload);
        }
        else
        {
            await _cola.EncolarAsync("Identity", "ActualizarPerfil", payload);
        }

        Mensaje = "Guardado. Se enviará cuando haya conexión.";
    }

    /// <summary>Pone en el formulario el teléfono que el servidor rechazó, para que se corrija y se envíe de nuevo.</summary>
    public Task CorregirAsync(OperacionPendiente rechazada)
    {
        using var datos = JsonDocument.Parse(rechazada.Payload);
        Telefono = datos.RootElement.TryGetProperty("telefono", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        _corrigiendo = rechazada.OperationId;
        Mensaje = $"El servidor dijo: {rechazada.MotivoRechazo} Corrige el teléfono y pulsa «Guardar teléfono».";
        return Task.CompletedTask;
    }
}
