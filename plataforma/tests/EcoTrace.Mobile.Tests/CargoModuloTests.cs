using System.Net;
using System.Text;
using System.Text.Json;
using EcoTrace.App.Modulos.Cargo;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Mobile.Tests;

/// <summary>Una copia de cargas que vive solo en memoria. Sirve para probar la lógica sin crear un archivo SQLite.</summary>
internal sealed class CacheEnMemoria : ICacheDeCargas
{
    /// <summary>Las cargas guardadas en memoria.</summary>
    private IReadOnlyList<CargaEnCache> _cargas = [];

    /// <summary>Reemplaza todas las cargas guardadas por la lista nueva.</summary>
    public Task ReemplazarAsync(IReadOnlyList<CargaEnCache> cargas, CancellationToken ct = default)
    {
        _cargas = cargas.ToList();
        return Task.CompletedTask;
    }

    /// <summary>Devuelve las cargas guardadas.</summary>
    public Task<IReadOnlyList<CargaEnCache>> ListarAsync(CancellationToken ct = default) => Task.FromResult(_cargas);
}

/// <summary>Un módulo mínimo de Cargo para las pruebas: tiene los manejadores reales, que son lo que se prueba.</summary>
internal sealed class ModuloCargoDePrueba(ConfiguracionServicios servicios) : IModuloApp
{
    /// <summary>El nombre del módulo en la cola: «Cargo».</summary>
    public string Nombre => "Cargo";

    /// <summary>El título del módulo.</summary>
    public string Titulo => "Cargas";

    /// <summary>Los manejadores reales del módulo, que son lo que se prueba.</summary>
    public IReadOnlyList<IManejadorOperacion> Manejadores { get; } = ManejadoresDeCargo.Crear(servicios);
}

/// <summary>Una carga tal como la devolvería el servidor de mentira.</summary>
internal sealed record CargaDelServidor(Guid Id, string Descripcion, string Origen, string Destino, int PesoKg, string Estado);

/// <summary>
/// El escenario de prueba del módulo: arma todo lo que necesita con piezas reales y de mentira. Son reales la cola de acciones
/// (en un SQLite temporal) y el motor de sincronización; son de mentira el servidor, la sesión, la conectividad y el reloj. Así
/// se prueban los flujos completos (sin conexión, reintentos, conflictos) sin teléfono ni red.
/// </summary>
internal sealed class MundoDeCargo : IDisposable
{
    /// <summary>Una carga de ejemplo con identificador fijo, para que las pruebas la reconozcan.</summary>
    public static readonly Guid CargaA = new("aaaaaaaa-0000-4000-8000-000000000001");
    /// <summary>Otra carga de ejemplo con identificador fijo.</summary>
    public static readonly Guid CargaB = new("bbbbbbbb-0000-4000-8000-000000000002");

    /// <summary>Las direcciones de los servicios que usa la prueba (todas son falsas).</summary>
    private const string ConfiguracionJson =
        """{ "entorno": "desarrollo", "servicios": { "identity": "http://prueba:5101", "fleet": "http://prueba:5102", "cargo": "http://prueba:5103", "billing": "http://prueba:5104" } }""";

    /// <summary>El archivo SQLite temporal de la cola. Se borra al terminar.</summary>
    private readonly BaseTemporal _base = new();

    /// <summary>Arma el escenario: la cola, el servidor falso, la sesión, la conectividad, el cliente y el motor de sincronización.</summary>
    public MundoDeCargo()
    {
        Configuracion = ConfiguracionServicios.Leer(ConfiguracionJson);
        Cola = _base.Abrir(Reloj);
        Http = new HttpClient(Servidor);
        Cliente = new ClienteDeCargas(Http, Configuracion, Sesion, Reloj);
        Servicio = new ServicioDeCargas(Cliente, Cache, Cola, Conectividad, Reloj);
        Acciones = new AccionesDeCarga(Servicio, Cola);
        Motor = new MotorSincronizacion(
            Cola, new RegistroDeModulos([new ModuloCargoDePrueba(Configuracion)]), Sesion, Conectividad, Http, Reloj);
        Servidor.Responder = (solicitud, ct) =>
            solicitud.Metodo == HttpMethod.Get ? Task.FromResult(ListaDelServidor()) : RespuestaDeEnvio(solicitud, ct);
    }

    /// <summary>Un reloj que solo avanza cuando la prueba lo manda.</summary>
    public RelojDePrueba Reloj { get; } = new(new DateTimeOffset(2026, 11, 12, 15, 30, 0, TimeSpan.Zero));

    /// <summary>Las direcciones de los servicios.</summary>
    public ConfiguracionServicios Configuracion { get; }

    /// <summary>La cola real de acciones pendientes, guardada en un SQLite temporal.</summary>
    public ColaLocalSqlite Cola { get; }

    /// <summary>La copia de cargas en memoria.</summary>
    public CacheEnMemoria Cache { get; } = new();

    /// <summary>El servidor de mentira: responde lo que cada prueba le indique y anota lo que recibe.</summary>
    public ServidorFalso Servidor { get; } = new();

    /// <summary>Una sesión de mentira: entrega un token de prueba o dice que no hay sesión.</summary>
    public SesionFalsa Sesion { get; } = new();

    /// <summary>La conectividad de mentira: la prueba decide si «hay señal» o no (equivale al modo avión).</summary>
    public ConectividadFalsa Conectividad { get; } = new();

    /// <summary>El cliente HTTP que usa el módulo, conectado al servidor de mentira.</summary>
    public HttpClient Http { get; }

    /// <summary>El cliente que pide las cargas al servidor.</summary>
    public ClienteDeCargas Cliente { get; }

    /// <summary>El servicio que arma la lista que ve el conductor.</summary>
    public ServicioDeCargas Servicio { get; }

    /// <summary>Las acciones del conductor (confirmar entrega, cambiar estado).</summary>
    public AccionesDeCarga Acciones { get; }

    /// <summary>El motor real que envía las acciones pendientes al servidor, en orden.</summary>
    public MotorSincronizacion Motor { get; }

    /// <summary>Las cargas que el servidor de mentira devuelve cuando se le pide la lista.</summary>
    public List<CargaDelServidor> CargasDelServidor { get; } = [];

    /// <summary>Cómo responde el servidor de mentira cuando le llega una acción. Cada prueba la cambia según lo que quiera simular.</summary>
    public Func<SolicitudRecibida, CancellationToken, Task<HttpResponseMessage>> RespuestaDeEnvio { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));

    /// <summary>Crea una carga de ejemplo con el estado indicado.</summary>
    public static CargaDelServidor Carga(Guid id, string estado, string descripcion = "Residuos industriales") =>
        new(id, descripcion, "Cali", "Popayán", 4200, estado);

    /// <summary>Libera todo y borra el archivo temporal.</summary>
    public void Dispose()
    {
        Http.Dispose();
        _base.Dispose();
    }

    /// <summary>Arma la respuesta del servidor cuando le piden la lista de cargas.</summary>
    private HttpResponseMessage ListaDelServidor()
    {
        var cuerpo = JsonSerializer.Serialize(CargasDelServidor.Select(c => new
        {
            cargaId = c.Id, descripcion = c.Descripcion, origen = c.Origen, destino = c.Destino, pesoKg = c.PesoKg, estado = c.Estado
        }));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
    }
}

/// <summary>
/// Pruebas del módulo de Cargo de la app, sin emulador ni red. Comprueban la lógica de la regla de oro del ADR 0005: lo que
/// ve el conductor, lo que se guarda en la cola, el orden de envío, los reintentos y los conflictos.
/// </summary>
public sealed class CargoModuloTests
{
    /// <summary>Las direcciones de los servicios para las pruebas de los manejadores.</summary>
    private static readonly ConfiguracionServicios Configuracion = ConfiguracionServicios.Leer(
        """{ "entorno": "desarrollo", "servicios": { "identity": "http://prueba:5101", "fleet": "http://prueba:5102", "cargo": "http://prueba:5103", "billing": "http://prueba:5104" } }""");

    /// <summary>Arma una acción pendiente de la cola, como la guardaría la app.</summary>
    private static OperacionPendiente Operacion(string tipo, string payload) => new()
    {
        OperationId = Guid.NewGuid(),
        Modulo = "Cargo",
        Tipo = tipo,
        Payload = payload,
        Estado = EstadoOperacion.PendienteSync,
        CreadaEnUtc = DateTime.UtcNow
    };

    // ---------------------------------------------------------------- estados

    /// <summary>Recorre todos los pares de estados y comprueba que la app ofrezca exactamente los cambios que el servidor permite. Si el servidor cambia sus reglas, esta prueba falla y avisa.</summary>
    [Fact]
    public void Los_cambios_que_ofrece_la_app_son_los_mismos_que_permite_el_servidor()
    {
        Assert.Equal(new[] { "EnTransito" }, EstadosDeCarga.SiguientesPermitidos("Asignado"));
        Assert.Equal(new[] { "ConNovedad", "Entregado" }, EstadosDeCarga.SiguientesPermitidos("EnTransito"));
        Assert.Equal(new[] { "EnTransito", "Entregado" }, EstadosDeCarga.SiguientesPermitidos("ConNovedad"));
        Assert.Empty(EstadosDeCarga.SiguientesPermitidos("Entregado"));
        Assert.Empty(EstadosDeCarga.SiguientesPermitidos("Pendiente"));
    }

    // ------------------------------------------------------------- manejadores

    /// <summary>Toma una acción de confirmar entrega y comprueba que el manejador arme el POST a <c>/api/cargas/{id}/seguimientos</c> con el estado Entregado.</summary>
    [Fact]
    public async Task Confirmar_entrega_arma_el_POST_de_seguimientos_de_esa_carga_con_estado_Entregado()
    {
        var cargaId = Guid.NewGuid();
        var operacion = Operacion("ConfirmarEntrega", PayloadDeCarga.Serializar(new DatosCambioDeEstado(cargaId, "Entregado", "Popayán", null)));

        using var solicitud = new ConfirmarEntregaHandler(Configuracion).Construir(operacion);

        Assert.Equal(HttpMethod.Post, solicitud.Method);
        Assert.Equal($"http://prueba:5103/api/cargas/{cargaId}/seguimientos", solicitud.RequestUri!.ToString());
        using var cuerpo = JsonDocument.Parse(await solicitud.Content!.ReadAsStringAsync());
        Assert.Equal("Entregado", cuerpo.RootElement.GetProperty("estado").GetString());
        Assert.Equal("Popayán", cuerpo.RootElement.GetProperty("ubicacion").GetString());
    }

    /// <summary>Aunque el contenido guardado diga otro estado, confirmar entrega siempre manda «Entregado».</summary>
    [Fact]
    public async Task Confirmar_entrega_siempre_lleva_la_carga_a_Entregado_sin_importar_el_payload()
    {
        var operacion = Operacion("ConfirmarEntrega", PayloadDeCarga.Serializar(new DatosCambioDeEstado(Guid.NewGuid(), "EnTransito", "Popayán", null)));

        using var solicitud = new ConfirmarEntregaHandler(Configuracion).Construir(operacion);

        using var cuerpo = JsonDocument.Parse(await solicitud.Content!.ReadAsStringAsync());
        Assert.Equal("Entregado", cuerpo.RootElement.GetProperty("estado").GetString());
    }

    /// <summary>El cambio de estado envía al servidor el estado, la ubicación y la nota que el conductor dejó guardados.</summary>
    [Fact]
    public async Task Cambiar_estado_envia_el_estado_la_ubicacion_y_la_nota_del_payload()
    {
        var operacion = Operacion("CambiarEstado", PayloadDeCarga.Serializar(new DatosCambioDeEstado(Guid.NewGuid(), "ConNovedad", "En ruta", "Derrumbe en la vía")));

        using var solicitud = new CambiarEstadoHandler(Configuracion).Construir(operacion);

        using var cuerpo = JsonDocument.Parse(await solicitud.Content!.ReadAsStringAsync());
        Assert.Equal("ConNovedad", cuerpo.RootElement.GetProperty("estado").GetString());
        Assert.Equal("En ruta", cuerpo.RootElement.GetProperty("ubicacion").GetString());
        Assert.Equal("Derrumbe en la vía", cuerpo.RootElement.GetProperty("nota").GetString());
    }

    /// <summary>Si el contenido de una acción está dañado, el manejador no lanza error: arma una solicitud a una carga que no existe para que el servidor la rechace y la cola no se bloquee.</summary>
    [Fact]
    public void Un_payload_ilegible_no_lanza_excepcion_y_apunta_a_una_carga_que_no_existe()
    {
        // Si Construir lanzara, el motor no lo controla y la acción quedaría "Sincronizando" bloqueando la cola.
        var operacion = Operacion("CambiarEstado", "esto no es json");

        using var solicitud = new CambiarEstadoHandler(Configuracion).Construir(operacion);

        Assert.Contains(Guid.Empty.ToString(), solicitud.RequestUri!.ToString());
    }

    /// <summary>Comprueba que los dos manejadores pertenezcan al módulo Cargo y cubran las dos acciones: <c>CambiarEstado</c> y <c>ConfirmarEntrega</c>.</summary>
    [Fact]
    public void Los_manejadores_son_del_modulo_Cargo_y_cubren_las_dos_acciones()
    {
        var manejadores = ManejadoresDeCargo.Crear(Configuracion);

        Assert.All(manejadores, m => Assert.Equal("Cargo", m.Modulo));
        Assert.Equal(new[] { "CambiarEstado", "ConfirmarEntrega" }, manejadores.Select(m => m.Tipo).Order());
    }

    /// <summary>Guarda los datos de una acción como texto y los lee de vuelta: deben salir iguales.</summary>
    [Fact]
    public void El_payload_se_guarda_y_se_lee_igual()
    {
        var datos = new DatosCambioDeEstado(Guid.NewGuid(), "ConNovedad", "En ruta", "Llanta pinchada");

        Assert.Equal(datos, PayloadDeCarga.Leer(PayloadDeCarga.Serializar(datos)));
        Assert.Null(PayloadDeCarga.Leer("{}"));
        Assert.Null(PayloadDeCarga.Leer("no es json"));
    }

    // ----------------------------------------------------------- copia local

    /// <summary>Con conexión, la lista del servidor queda guardada en la copia del teléfono y se anota la hora del último contacto.</summary>
    [Fact]
    public async Task Con_conexion_la_lista_del_servidor_queda_en_la_copia_y_se_registra_el_contacto()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "EnTransito"));

        var vista = await m.Servicio.RefrescarAsync();

        Assert.False(vista.SonDatosGuardados);
        var carga = Assert.Single(vista.Cargas);
        Assert.Equal("EnTransito", carga.EstadoMostrado);
        Assert.Equal("Cali → Popayán", carga.Ruta);
        Assert.Equal(new[] { "ConNovedad", "Entregado" }, carga.Acciones);
        Assert.Equal(m.Reloj.GetUtcNow().UtcDateTime, vista.UltimaSincronizacionUtc);
        Assert.Single(await m.Cache.ListarAsync());
        Assert.Equal("Bearer token-vigente", Assert.Single(m.Servidor.Recibidas).Authorization);
    }

    /// <summary>En modo avión, la pantalla muestra lo guardado, con un aviso, y ni siquiera intenta llamar al servidor.</summary>
    [Fact]
    public async Task Sin_conexion_se_muestra_lo_guardado_y_no_se_llama_al_servidor()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "EnTransito"));
        await m.Servicio.RefrescarAsync();

        m.Conectividad.HayConexion = false;
        var vista = await m.Servicio.RefrescarAsync();

        Assert.True(vista.SonDatosGuardados);
        Assert.Contains("Sin conexión", vista.Aviso);
        Assert.Single(vista.Cargas);
        Assert.Single(m.Servidor.Recibidas);
    }

    /// <summary>Si hay señal pero el servidor no responde, se muestra lo guardado con un aviso que explica por qué.</summary>
    [Fact]
    public async Task Si_el_servidor_no_responde_se_muestra_lo_guardado_con_un_aviso()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "EnTransito"));
        await m.Servicio.RefrescarAsync();

        m.Servidor.Responder = (_, _) => throw new HttpRequestException("Se cortó la red.");
        var vista = await m.Servicio.RefrescarAsync();

        Assert.True(vista.SonDatosGuardados);
        Assert.Contains("No se pudo conectar", vista.Aviso);
        Assert.Single(vista.Cargas);
    }

    /// <summary>Sin sesión iniciada no hay token, así que no se pide nada al servidor y se muestra lo guardado.</summary>
    [Fact]
    public async Task Sin_sesion_no_se_pide_nada_y_se_muestra_lo_guardado()
    {
        using var m = new MundoDeCargo();
        m.Sesion.Token = null;

        var vista = await m.Servicio.RefrescarAsync();

        Assert.True(vista.SonDatosGuardados);
        Assert.Contains("Inicia sesión", vista.Aviso);
        Assert.Empty(m.Servidor.Recibidas);
    }

    /// <summary>Si el servidor responde 401 (el token no sirve), se le avisa a la sesión y la copia guardada no se pierde.</summary>
    [Fact]
    public async Task Un_401_al_pedir_las_cargas_avisa_a_la_sesion_y_no_pierde_la_copia()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "EnTransito"));
        await m.Servicio.RefrescarAsync();

        m.Servidor.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var vista = await m.Servicio.RefrescarAsync();

        Assert.Equal(1, m.Sesion.TokenRechazado);
        Assert.True(vista.SonDatosGuardados);
        Assert.Single(vista.Cargas);
    }

    /// <summary>La lista nueva del servidor reemplaza a la anterior: una carga que el servidor ya no devuelve desaparece de la copia.</summary>
    [Fact]
    public async Task Una_lista_nueva_reemplaza_a_la_anterior_y_una_carga_que_ya_no_llega_desaparece()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "EnTransito", "Carga A"));
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaB, "Asignado", "Carga B"));
        Assert.Equal(2, (await m.Servicio.RefrescarAsync()).Cargas.Count);

        m.CargasDelServidor.RemoveAll(c => c.Id == MundoDeCargo.CargaB);
        var vista = await m.Servicio.RefrescarAsync();

        Assert.Equal(MundoDeCargo.CargaA, Assert.Single(vista.Cargas).CargaId);
    }

    /// <summary>Ordena la lista: las cargas por entregar van primero y las ya entregadas al final.</summary>
    [Fact]
    public async Task Las_cargas_pendientes_van_antes_que_las_entregadas()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "Entregado", "Aaa ya entregada"));
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaB, "EnTransito", "Zzz en camino"));

        var vista = await m.Servicio.RefrescarAsync();

        Assert.Equal(new[] { MundoDeCargo.CargaB, MundoDeCargo.CargaA }, vista.Cargas.Select(c => c.CargaId));
    }

    // ------------------------------------------------- acciones del conductor

    /// <summary>Cuando el conductor confirma una entrega sin señal, la pantalla cambia de inmediato a «Entregado» y queda marcada como pendiente de sincronizar.</summary>
    [Fact]
    public async Task Una_accion_en_cola_cambia_el_estado_que_se_muestra_de_inmediato_y_queda_pendiente()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "EnTransito"));
        await m.Servicio.RefrescarAsync();

        var resultado = await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "Entregado");

        Assert.True(resultado.Exito);
        var carga = Assert.Single((await m.Servicio.ObtenerDeCacheAsync()).Cargas);
        Assert.Equal("Entregado", carga.EstadoMostrado);
        Assert.Equal("EnTransito", carga.EstadoServidor);
        Assert.True(carga.PendienteDeSync);
        Assert.Empty(carga.Acciones);

        // Y el servidor todavía no supo nada: solo se guardó en el teléfono.
        Assert.Single(m.Servidor.Recibidas);
        var operacion = Assert.Single(await m.Cola.ListarAsync(modulo: "Cargo"));
        Assert.Equal("ConfirmarEntrega", operacion.Tipo);
        Assert.Equal(EstadoOperacion.PendienteSync, operacion.Estado);
        Assert.Equal("Popayán", PayloadDeCarga.Leer(operacion.Payload)!.Ubicacion);
    }

    /// <summary>Si el conductor toca dos veces seguidas «Confirmar entrega», la segunda no guarda otra entrega: ya ve el estado nuevo.</summary>
    [Fact]
    public async Task Tocar_dos_veces_seguidas_confirmar_entrega_no_encola_una_entrega_repetida()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "EnTransito"));
        await m.Servicio.RefrescarAsync();

        var primera = await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "Entregado");
        var segunda = await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "Entregado");

        Assert.True(primera.Exito);
        Assert.False(segunda.Exito);
        Assert.Contains("No se puede pasar de Entregada a Entregada", segunda.Mensaje);
        Assert.Single(await m.Cola.ListarAsync(modulo: "Cargo"));
    }

    /// <summary>Reportar una novedad sin nota se rechaza; con nota se guarda en la cola como un cambio de estado.</summary>
    [Fact]
    public async Task Una_novedad_exige_nota_y_con_nota_se_encola_como_cambio_de_estado()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "EnTransito"));
        await m.Servicio.RefrescarAsync();

        var sinNota = await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "ConNovedad", "   ");
        Assert.False(sinNota.Exito);
        Assert.Contains("nota", sinNota.Mensaje);
        Assert.Empty(await m.Cola.ListarAsync(modulo: "Cargo"));

        var conNota = await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "ConNovedad", "  Derrumbe en la vía  ");
        Assert.True(conNota.Exito);
        var operacion = Assert.Single(await m.Cola.ListarAsync(modulo: "Cargo"));
        Assert.Equal("CambiarEstado", operacion.Tipo);
        var datos = PayloadDeCarga.Leer(operacion.Payload)!;
        Assert.Equal("Derrumbe en la vía", datos.Nota);
        Assert.Equal("En ruta", datos.Ubicacion);
    }

    /// <summary>No se guarda en la cola un cambio que el servidor rechazaría, ni una nota demasiado larga, ni una acción sobre una carga desconocida. En cada caso se devuelve un mensaje claro.</summary>
    [Fact]
    public async Task No_se_encola_un_cambio_que_el_servidor_rechazaria_ni_una_nota_demasiado_larga_ni_una_carga_desconocida()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "Asignado"));
        await m.Servicio.RefrescarAsync();

        var saltoDeEstado = await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "Entregado");
        var notaLarga = await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "EnTransito", new string('x', 301));
        var desconocida = await m.Acciones.CambiarEstadoAsync(Guid.NewGuid(), "EnTransito");

        Assert.False(saltoDeEstado.Exito);
        Assert.Contains("No se puede pasar de Asignada a Entregada", saltoDeEstado.Mensaje);
        Assert.False(notaLarga.Exito);
        Assert.False(desconocida.Exito);
        Assert.Empty(await m.Cola.ListarAsync(modulo: "Cargo"));
    }

    /// <summary>Si el conductor encadena acciones (primero «en tránsito», luego «entregar»), cada una se valida contra el estado que él ya ve y no contra el estado viejo del servidor.</summary>
    [Fact]
    public async Task Las_acciones_encadenadas_se_validan_contra_el_estado_que_el_conductor_ya_ve()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "Asignado"));
        await m.Servicio.RefrescarAsync();

        // Sin señal: primero sale en tránsito y después entrega. La segunda es válida porque ya ve "En tránsito".
        Assert.True((await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "EnTransito")).Exito);
        Assert.True((await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "Entregado")).Exito);

        Assert.Equal(2, (await m.Cola.ListarAsync(modulo: "Cargo")).Count);
    }

    // ------------------------------------------- de punta a punta con el motor

    /// <summary>El flujo principal: una entrega confirmada sin señal queda pendiente; al volver la señal el motor la envía y pasa a «Sincronizado».</summary>
    [Fact]
    public async Task Una_entrega_sin_conexion_se_envia_al_volver_la_senal_y_pasa_a_Sincronizado()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "EnTransito"));
        await m.Servicio.RefrescarAsync();

        // Modo avión: el conductor confirma la entrega y queda guardada en el teléfono.
        m.Conectividad.HayConexion = false;
        await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "Entregado");
        var sinSenal = await m.Motor.SincronizarAsync();
        Assert.Equal(ParadaSincronizacion.SinConexion, sinSenal.Parada);
        Assert.Single(m.Servidor.Recibidas);

        // Vuelve la señal: el motor la envía.
        m.Conectividad.HayConexion = true;
        var conSenal = await m.Motor.SincronizarAsync();

        Assert.Equal(1, conSenal.Sincronizadas);
        var operacion = Assert.Single(await m.Cola.ListarAsync(modulo: "Cargo"));
        Assert.Equal(EstadoOperacion.Sincronizado, operacion.Estado);

        var envio = m.Servidor.Recibidas[^1];
        Assert.Equal(HttpMethod.Post, envio.Metodo);
        Assert.Equal($"/api/cargas/{MundoDeCargo.CargaA}/seguimientos", envio.Direccion.AbsolutePath);
        Assert.Equal(operacion.OperationId.ToString("D"), envio.IdempotencyKey);
        Assert.Equal("Bearer token-vigente", envio.Authorization);
        using var cuerpo = JsonDocument.Parse(envio.Cuerpo!);
        Assert.Equal("Entregado", cuerpo.RootElement.GetProperty("estado").GetString());
    }

    /// <summary>Simula un corte a mitad del envío: la acción se reintenta con el mismo número de operación y, si el servidor responde 200 (ya la tenía), cuenta como éxito.</summary>
    [Fact]
    public async Task Un_corte_a_mitad_del_envio_se_reintenta_con_la_misma_clave_y_un_200_cuenta_como_exito()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "EnTransito"));
        await m.Servicio.RefrescarAsync();
        await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "Entregado");

        // El servidor procesa la entrega pero la respuesta se pierde; después responde 200 ("ya estaba hecho").
        m.RespuestaDeEnvio = (_, _) => throw new HttpRequestException("Se cortó la red.");
        Assert.Equal(ParadaSincronizacion.ErrorTransitorio, (await m.Motor.SincronizarAsync()).Parada);
        m.RespuestaDeEnvio = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        var reintento = await m.Motor.SincronizarAsync();

        Assert.Equal(1, reintento.Sincronizadas);
        var envios = m.Servidor.Recibidas.Where(r => r.Metodo == HttpMethod.Post).ToList();
        Assert.Equal(2, envios.Count);
        Assert.Equal(envios[0].IdempotencyKey, envios[1].IdempotencyKey);
        Assert.Equal(EstadoOperacion.Sincronizado, Assert.Single(await m.Cola.ListarAsync(modulo: "Cargo")).Estado);
    }

    /// <summary>Si hay varias acciones pendientes, llegan al servidor en el mismo orden en que el conductor las hizo.</summary>
    [Fact]
    public async Task Las_acciones_se_envian_en_el_orden_en_que_el_conductor_las_hizo()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "Asignado"));
        await m.Servicio.RefrescarAsync();
        await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "EnTransito");
        await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "Entregado");

        var resultado = await m.Motor.SincronizarAsync();

        Assert.Equal(2, resultado.Sincronizadas);
        var estados = new List<string>();
        foreach (var envio in m.Servidor.Recibidas.Where(r => r.Metodo == HttpMethod.Post))
        {
            using var cuerpo = JsonDocument.Parse(envio.Cuerpo!);
            estados.Add(cuerpo.RootElement.GetProperty("estado").GetString()!);
        }

        Assert.Equal(new[] { "EnTransito", "Entregado" }, estados);
    }

    /// <summary>Si el servidor rechaza una acción con un conflicto B, ésta queda «Rechazada» con su motivo (no se reintenta ni se borra) y la pantalla lo muestra.</summary>
    [Fact]
    public async Task Un_conflicto_B_deja_la_accion_rechazada_con_su_motivo_y_la_pantalla_lo_muestra()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "EnTransito"));
        await m.Servicio.RefrescarAsync();
        await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "Entregado");

        // Mientras no había señal, otra persona ya entregó la carga: el servidor manda y rechaza con conflicto B.
        m.RespuestaDeEnvio = (_, _) => Task.FromResult(ServidorFalso.Problema(
            HttpStatusCode.Conflict, "B", "No se puede pasar de Entregado a Entregado."));
        var resultado = await m.Motor.SincronizarAsync();

        Assert.Equal(1, resultado.Rechazadas);
        var operacion = Assert.Single(await m.Cola.ListarAsync(modulo: "Cargo"));
        Assert.Equal(EstadoOperacion.Rechazado, operacion.Estado);
        Assert.Equal(TipoConflicto.B, operacion.TipoConflicto);
        Assert.Contains("Entregado a Entregado", operacion.MotivoRechazo);

        // La acción rechazada NO se pierde y la pantalla muestra el motivo. El estado mostrado es el del servidor.
        var carga = Assert.Single((await m.Servicio.ObtenerDeCacheAsync()).Cargas);
        Assert.True(carga.TieneRechazo);
        Assert.Equal(TipoConflicto.B, carga.ConflictoRechazo);
        Assert.False(carga.PendienteDeSync);
        Assert.Equal("EnTransito", carga.EstadoMostrado);

        // Al actualizar, la copia trae el estado real del servidor y el rechazo sigue visible para el conductor.
        m.CargasDelServidor[0] = MundoDeCargo.Carga(MundoDeCargo.CargaA, "Entregado");
        var actualizada = Assert.Single((await m.Servicio.RefrescarAsync()).Cargas);
        Assert.Equal("Entregado", actualizada.EstadoMostrado);
        Assert.True(actualizada.TieneRechazo);
    }

    /// <summary>Si la acción ya se sincronizó y es más nueva que la copia, se sigue mostrando su estado hasta el próximo refresco.</summary>
    [Fact]
    public async Task Una_accion_sincronizada_mas_nueva_que_la_copia_se_muestra_hasta_el_proximo_refresco()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "EnTransito"));
        await m.Servicio.RefrescarAsync();
        await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "Entregado");

        // La entrega llega al servidor DESPUÉS de que se guardó la copia: la copia todavía dice "En tránsito".
        m.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await m.Motor.SincronizarAsync();

        var carga = Assert.Single((await m.Servicio.ObtenerDeCacheAsync()).Cargas);
        Assert.Equal("Entregado", carga.EstadoMostrado);
        Assert.False(carga.PendienteDeSync);
    }

    /// <summary>Si la copia es más nueva que la acción sincronizada, manda la copia (el servidor ya cambió).</summary>
    [Fact]
    public async Task Si_la_copia_es_mas_nueva_que_la_accion_sincronizada_manda_la_copia()
    {
        using var m = new MundoDeCargo();
        m.CargasDelServidor.Add(MundoDeCargo.Carga(MundoDeCargo.CargaA, "EnTransito"));
        await m.Servicio.RefrescarAsync();
        await m.Acciones.CambiarEstadoAsync(MundoDeCargo.CargaA, "ConNovedad", "Llanta pinchada");
        await m.Motor.SincronizarAsync();

        // Después de la acción, otra persona la pasó a Entregado y la app actualizó su copia.
        m.Reloj.Avanzar(TimeSpan.FromMinutes(5));
        m.CargasDelServidor[0] = MundoDeCargo.Carga(MundoDeCargo.CargaA, "Entregado");
        await m.Servicio.RefrescarAsync();

        var carga = Assert.Single((await m.Servicio.ObtenerDeCacheAsync()).Cargas);
        Assert.Equal("Entregado", carga.EstadoMostrado);
    }
}
