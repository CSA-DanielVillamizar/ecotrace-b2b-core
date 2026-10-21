using System.Net;
using System.Text;
using System.Text.Json;
using EcoTrace.App.Modulos.Cargo;
using EcoTrace.Mobile.Core.Cola;
using EcoTrace.Mobile.Core.Modulos;
using EcoTrace.Mobile.Core.Sincronizacion;

namespace EcoTrace.Mobile.Tests;

/// <summary>Copia de cargas en memoria, para probar la lógica del módulo sin tocar SQLite (eso lo prueba otra clase).</summary>
internal sealed class CacheEnMemoria : ICacheDeCargas
{
    private IReadOnlyList<CargaEnCache> _cargas = [];

    public Task ReemplazarAsync(IReadOnlyList<CargaEnCache> cargas, CancellationToken ct = default)
    {
        _cargas = cargas.ToList();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CargaEnCache>> ListarAsync(CancellationToken ct = default) => Task.FromResult(_cargas);
}

/// <summary>El módulo de Cargo tal como lo ve el registro de la app: su nombre y los manejadores reales.</summary>
internal sealed class ModuloCargoDePrueba(ConfiguracionServicios servicios) : IModuloApp
{
    public string Nombre => "Cargo";

    public string Titulo => "Cargas";

    public IReadOnlyList<IManejadorOperacion> Manejadores { get; } = ManejadoresDeCargo.Crear(servicios);
}

internal sealed record CargaDelServidor(Guid Id, string Descripcion, string Origen, string Destino, int PesoKg, string Estado);

/// <summary>
/// El módulo de Cargo armado igual que en la app, con las piezas reales (servicio, acciones, manejadores, motor de
/// sincronización y la cola del Mobile.Core) y solo el servidor y la red falsos.
/// </summary>
internal sealed class MundoDeCargo : IDisposable
{
    public static readonly Guid CargaA = new("aaaaaaaa-0000-4000-8000-000000000001");
    public static readonly Guid CargaB = new("bbbbbbbb-0000-4000-8000-000000000002");

    private const string ConfiguracionJson =
        """{ "entorno": "desarrollo", "servicios": { "identity": "http://prueba:5101", "fleet": "http://prueba:5102", "cargo": "http://prueba:5103", "billing": "http://prueba:5104" } }""";

    private readonly BaseTemporal _base = new();

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

    public RelojDePrueba Reloj { get; } = new(new DateTimeOffset(2026, 11, 12, 15, 30, 0, TimeSpan.Zero));

    public ConfiguracionServicios Configuracion { get; }

    public ColaLocalSqlite Cola { get; }

    public CacheEnMemoria Cache { get; } = new();

    public ServidorFalso Servidor { get; } = new();

    public SesionFalsa Sesion { get; } = new();

    public ConectividadFalsa Conectividad { get; } = new();

    public HttpClient Http { get; }

    public ClienteDeCargas Cliente { get; }

    public ServicioDeCargas Servicio { get; }

    public AccionesDeCarga Acciones { get; }

    public MotorSincronizacion Motor { get; }

    /// <summary>Las cargas que el servidor devuelve en <c>GET /api/cargas</c>. La prueba las cambia cuando quiere.</summary>
    public List<CargaDelServidor> CargasDelServidor { get; } = [];

    /// <summary>Cómo responde el servidor a un envío de la cola (POST). Por defecto acepta con 201.</summary>
    public Func<SolicitudRecibida, CancellationToken, Task<HttpResponseMessage>> RespuestaDeEnvio { get; set; } =
        (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));

    public static CargaDelServidor Carga(Guid id, string estado, string descripcion = "Residuos industriales") =>
        new(id, descripcion, "Cali", "Popayán", 4200, estado);

    public void Dispose()
    {
        Http.Dispose();
        _base.Dispose();
    }

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
/// El módulo de Cargo de la app, sin pantalla: los manejadores que arman la solicitud al servidor, la copia local,
/// la lista que ve el conductor y las acciones que guarda en la cola. Todo corre sin emulador.
/// </summary>
public sealed class CargoModuloTests
{
    private static readonly ConfiguracionServicios Configuracion = ConfiguracionServicios.Leer(
        """{ "entorno": "desarrollo", "servicios": { "identity": "http://prueba:5101", "fleet": "http://prueba:5102", "cargo": "http://prueba:5103", "billing": "http://prueba:5104" } }""");

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

    [Fact]
    public async Task Confirmar_entrega_siempre_lleva_la_carga_a_Entregado_sin_importar_el_payload()
    {
        var operacion = Operacion("ConfirmarEntrega", PayloadDeCarga.Serializar(new DatosCambioDeEstado(Guid.NewGuid(), "EnTransito", "Popayán", null)));

        using var solicitud = new ConfirmarEntregaHandler(Configuracion).Construir(operacion);

        using var cuerpo = JsonDocument.Parse(await solicitud.Content!.ReadAsStringAsync());
        Assert.Equal("Entregado", cuerpo.RootElement.GetProperty("estado").GetString());
    }

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

    [Fact]
    public void Un_payload_ilegible_no_lanza_excepcion_y_apunta_a_una_carga_que_no_existe()
    {
        // Si Construir lanzara, el motor no lo controla y la acción quedaría "Sincronizando" bloqueando la cola.
        var operacion = Operacion("CambiarEstado", "esto no es json");

        using var solicitud = new CambiarEstadoHandler(Configuracion).Construir(operacion);

        Assert.Contains(Guid.Empty.ToString(), solicitud.RequestUri!.ToString());
    }

    [Fact]
    public void Los_manejadores_son_del_modulo_Cargo_y_cubren_las_dos_acciones()
    {
        var manejadores = ManejadoresDeCargo.Crear(Configuracion);

        Assert.All(manejadores, m => Assert.Equal("Cargo", m.Modulo));
        Assert.Equal(new[] { "CambiarEstado", "ConfirmarEntrega" }, manejadores.Select(m => m.Tipo).Order());
    }

    [Fact]
    public void El_payload_se_guarda_y_se_lee_igual()
    {
        var datos = new DatosCambioDeEstado(Guid.NewGuid(), "ConNovedad", "En ruta", "Llanta pinchada");

        Assert.Equal(datos, PayloadDeCarga.Leer(PayloadDeCarga.Serializar(datos)));
        Assert.Null(PayloadDeCarga.Leer("{}"));
        Assert.Null(PayloadDeCarga.Leer("no es json"));
    }

    // ----------------------------------------------------------- copia local

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
