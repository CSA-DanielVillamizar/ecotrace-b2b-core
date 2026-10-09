using System.Net;
using System.Text;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Mobile.Tests;

/// <summary>Un archivo SQLite temporal por prueba, que se borra al terminar.</summary>
internal sealed class BaseTemporal : IDisposable
{
    public BaseTemporal() => Ruta = Path.Combine(Path.GetTempPath(), $"ecotrace-cola-{Guid.NewGuid():N}.db");

    public string Ruta { get; }

    public ColaLocalSqlite Abrir(TimeProvider? reloj = null) => new(Ruta, reloj);

    public void Dispose()
    {
        foreach (var archivo in new[] { Ruta, Ruta + "-wal", Ruta + "-shm" })
        {
            try
            {
                File.Delete(archivo);
            }
            catch (IOException)
            {
                // Si el sistema todavía lo tiene abierto, lo limpia el directorio temporal.
            }
        }
    }
}

internal sealed class RelojDePrueba(DateTimeOffset inicio) : TimeProvider
{
    private DateTimeOffset _ahora = inicio;

    public override DateTimeOffset GetUtcNow() => _ahora;

    public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
}

/// <summary>Un servidor falso: responde lo que la prueba diga y guarda cada solicitud que recibió.</summary>
internal sealed class ServidorFalso : HttpMessageHandler
{
    private readonly List<SolicitudRecibida> _recibidas = [];
    private readonly object _candado = new();

    public Func<SolicitudRecibida, Task<HttpResponseMessage>> Responder { get; set; } =
        _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));

    public IReadOnlyList<SolicitudRecibida> Recibidas
    {
        get
        {
            lock (_candado)
            {
                return _recibidas.ToList();
            }
        }
    }

    public static HttpResponseMessage Problema(HttpStatusCode estado, string? tipoConflicto, string detalle)
    {
        var tipo = tipoConflicto is null ? string.Empty : $", \"tipoConflicto\": \"{tipoConflicto}\"";
        return new HttpResponseMessage(estado)
        {
            Content = new StringContent($"{{ \"title\": \"Conflicto\", \"detail\": \"{detalle}\"{tipo} }}", Encoding.UTF8, "application/problem+json")
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        var recibida = new SolicitudRecibida(
            solicitud.Method, solicitud.RequestUri!, solicitud.Headers.Authorization?.ToString(),
            Encabezado(solicitud, "Idempotency-Key"), Encabezado(solicitud, "X-Correlation-Id"),
            solicitud.Content is null ? null : await solicitud.Content.ReadAsStringAsync(ct));

        lock (_candado)
        {
            _recibidas.Add(recibida);
        }

        return await Responder(recibida);
    }

    private static string? Encabezado(HttpRequestMessage solicitud, string nombre) =>
        solicitud.Headers.TryGetValues(nombre, out var valores) ? string.Join(",", valores) : null;
}

internal sealed record SolicitudRecibida(
    HttpMethod Metodo, Uri Direccion, string? Authorization, string? IdempotencyKey, string? CorrelationId, string? Cuerpo);

internal sealed class SesionFalsa : IProveedorDeSesion
{
    public string? Token { get; set; } = "token-vigente";

    public int TokenRechazado { get; private set; }

    public Task<string?> ObtenerTokenVigenteAsync(CancellationToken ct = default) => Task.FromResult(Token);

    public Task NotificarTokenRechazadoAsync(CancellationToken ct = default)
    {
        TokenRechazado++;
        return Task.CompletedTask;
    }
}

internal sealed class ConectividadFalsa : IConectividad
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

/// <summary>Un módulo con un manejador por tipo: POST a /api/&lt;tipo&gt; con el payload tal cual.</summary>
internal sealed class ModuloDePrueba(string nombre, params string[] tipos) : IModuloApp
{
    public string Nombre => nombre;

    public string Titulo => nombre;

    public IReadOnlyList<IManejadorOperacion> Manejadores { get; } =
        tipos.Select(t => (IManejadorOperacion)new ManejadorDePrueba(nombre, t)).ToList();
}

internal sealed class ManejadorDePrueba(string modulo, string tipo) : IManejadorOperacion
{
    public string Modulo => modulo;

    public string Tipo => tipo;

    public HttpRequestMessage Construir(OperacionPendiente operacion) =>
        new(HttpMethod.Post, new Uri($"http://servicio.prueba/api/{tipo}"))
        {
            Content = new StringContent(operacion.Payload, Encoding.UTF8, "application/json")
        };
}

internal sealed class Entorno : IDisposable
{
    private readonly BaseTemporal _base = new();

    public Entorno(params string[] tiposDeCargo)
    {
        Cola = _base.Abrir();
        Registro = new RegistroDeModulos([new ModuloDePrueba("Cargo", tiposDeCargo.Length == 0 ? ["ConfirmarEntrega"] : tiposDeCargo)]);
        Http = new HttpClient(Servidor);
        Motor = new MotorSincronizacion(Cola, Registro, Sesion, Conectividad, Http);
    }

    public ColaLocalSqlite Cola { get; }

    public RegistroDeModulos Registro { get; }

    public ServidorFalso Servidor { get; } = new();

    public SesionFalsa Sesion { get; } = new();

    public ConectividadFalsa Conectividad { get; } = new();

    public HttpClient Http { get; }

    public MotorSincronizacion Motor { get; }

    public void Dispose()
    {
        Http.Dispose();
        _base.Dispose();
    }
}
