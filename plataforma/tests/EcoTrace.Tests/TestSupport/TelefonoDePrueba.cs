using EcoTrace.App.Modulos.Identity.Logica;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Tests.TestSupport;

/// <summary>
/// Un teléfono con la app, sin MAUI: la cola SQLite y el motor reales del Mobile.Core, la lógica del módulo Identity
/// de la app, y un HttpClient que llega al servicio en memoria. Lo único simulado es la señal, el reloj del teléfono
/// y el almacén seguro (en el teléfono es SecureStorage).
/// </summary>
public sealed class TelefonoDePrueba : IDisposable
{
    private readonly string _archivoCola = Path.Combine(Path.GetTempPath(), $"ecotrace-telefono-{Guid.NewGuid():N}.db");
    private readonly HttpClient _http;

    public TelefonoDePrueba(Func<HttpMessageHandler> red)
    {
        Red = new RedDelTelefono(red(), this);
        _http = new HttpClient(Red);

        var servicios = new ConfiguracionServicios(
            "desarrollo", new Uri("http://localhost"), new Uri("http://localhost"), new Uri("http://localhost"), new Uri("http://localhost"));
        var cliente = new ClienteIdentity(_http, servicios.Identity);

        Cola = new ColaLocalSqlite(_archivoCola, Reloj);
        Sesion = new GestorDeSesion(Almacen, cliente, Conectividad, Cola, Reloj);
        Perfil = new ServicioDePerfil(Sesion, cliente, Cola, Almacen, Reloj);
        Motor = new MotorSincronizacion(Cola, new RegistroDeModulos([new ModuloIdentityDeLaApp(servicios)]), Sesion, Conectividad, _http, Reloj);
    }

    public RelojDelTelefono Reloj { get; } = new();

    public SenalDelTelefono Conectividad { get; } = new();

    public AlmacenSeguroDePrueba Almacen { get; } = new();

    public RedDelTelefono Red { get; }

    public ColaLocalSqlite Cola { get; }

    public GestorDeSesion Sesion { get; }

    public ServicioDePerfil Perfil { get; }

    public MotorSincronizacion Motor { get; }

    public void Dispose()
    {
        _http.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var archivo in new[] { _archivoCola, _archivoCola + "-wal", _archivoCola + "-shm" })
        {
            try
            {
                File.Delete(archivo);
            }
            catch (IOException)
            {
                // Lo limpia el directorio temporal.
            }
        }
    }

    /// <summary>El módulo Identity de la app tal como lo registra ModuloIdentity, sin la parte de MAUI.</summary>
    private sealed class ModuloIdentityDeLaApp(ConfiguracionServicios servicios) : IModuloApp
    {
        public string Nombre => AccionesDeIdentity.Modulo;

        public string Titulo => "Perfil";

        public IReadOnlyList<IManejadorOperacion> Manejadores { get; } =
            [new ActualizarPerfilHandler(servicios), new CerrarOtrasSesionesHandler(servicios)];
    }
}

/// <summary>
/// La red del teléfono. Sin señal, falla como en modo avión. Con <see cref="PerderProximaRespuesta"/>, deja llegar la
/// solicitud al servidor pero pierde la respuesta: el caso exacto que obliga a reenviar con la misma clave.
/// </summary>
public sealed class RedDelTelefono(HttpMessageHandler servidor, TelefonoDePrueba telefono) : DelegatingHandler(servidor)
{
    private int _perder;

    public void PerderProximaRespuesta() => Interlocked.Exchange(ref _perder, 1);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        if (!telefono.Conectividad.HayConexion)
        {
            throw new HttpRequestException("Sin señal (modo avión simulado).");
        }

        var respuesta = await base.SendAsync(solicitud, ct);
        if (Interlocked.Exchange(ref _perder, 0) == 1)
        {
            respuesta.Dispose();
            throw new HttpRequestException("La respuesta se perdió en el camino (simulado).");
        }

        return respuesta;
    }
}

public sealed class SenalDelTelefono : IConectividad
{
    private bool _hayConexion = true;

    public bool HayConexion
    {
        get => _hayConexion;
        set
        {
            _hayConexion = value;
            CambioDeConexion?.Invoke(this, value);
        }
    }

    public event EventHandler<bool>? CambioDeConexion;
}

/// <summary>El reloj del teléfono: el real, más lo que la prueba lo adelante (para que venza el access token).</summary>
public sealed class RelojDelTelefono : TimeProvider
{
    private TimeSpan _adelanto;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _adelanto;

    public void Adelantar(TimeSpan tiempo) => _adelanto += tiempo;
}

public sealed class AlmacenSeguroDePrueba : IAlmacenSeguro
{
    private readonly Dictionary<string, string> _valores = [];

    public IReadOnlyDictionary<string, string> Valores => _valores;

    public Task<string?> LeerAsync(string clave) => Task.FromResult(_valores.GetValueOrDefault(clave));

    public Task GuardarAsync(string clave, string valor)
    {
        _valores[clave] = valor;
        return Task.CompletedTask;
    }

    public Task BorrarAsync(string clave)
    {
        _valores.Remove(clave);
        return Task.CompletedTask;
    }
}
