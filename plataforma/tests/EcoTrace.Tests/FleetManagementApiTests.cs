using System.Net;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

public sealed class FleetManagementApiTests(ApiFactory<FleetManagementApiMarker> fabrica)
    : IClassFixture<ApiFactory<FleetManagementApiMarker>>
{
    private readonly ApiFactory<FleetManagementApiMarker> _fabrica = fabrica;
    private readonly HttpClient _cliente = fabrica.CreateClient();

    // Fleet Management no consulta a Identity: los TenantId y UserId de estas pruebas son Guid
    // inventados. Que el servicio los acepte es la prueba de que solo guarda el identificador.
    private static object Vehiculo(Guid tenantId, string placa, int capacidadKg = 5000) =>
        new { tenantId, registradoPorUserId = Guid.NewGuid(), placa, capacidadKg };

    private static object Conductor(Guid tenantId, string licencia) =>
        new { tenantId, registradoPorUserId = Guid.NewGuid(), nombre = "Carlos Ruiz", licencia };

    private static object Asignacion(Guid cargaId, Guid vehiculoId, Guid conductorId) =>
        new { cargaId, vehiculoId, conductorId };

    private static string PlacaNueva() => "T" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static string LicenciaNueva() => "LIC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static bool CambioEstado(Respuesta respuesta) => respuesta.Propiedad("cambioEstado").GetBoolean();

    /// <summary>Registra un vehiculo y un conductor del mismo transportista, listos para reservar.</summary>
    private async Task<(Guid VehiculoId, Guid ConductorId)> CrearFlotaAsync(Guid? tenantId = null)
    {
        var transportista = tenantId ?? Guid.NewGuid();
        var vehiculo = await _cliente.PostearAsync("/api/vehiculos", Vehiculo(transportista, PlacaNueva()));
        var conductor = await _cliente.PostearAsync("/api/conductores", Conductor(transportista, LicenciaNueva()));

        return (vehiculo.Id("vehiculoId"), conductor.Id("conductorId"));
    }

    [Fact]
    public async Task Guarda_los_identificadores_externos_sin_validarlos_contra_identity()
    {
        var tenantId = Guid.NewGuid();
        var respuesta = await _cliente.PostearAsync("/api/vehiculos", Vehiculo(tenantId, PlacaNueva()));

        Assert.Equal(HttpStatusCode.Created, respuesta.Estado);
        Assert.Equal(tenantId, respuesta.Id("tenantId"));
    }

    [Fact]
    public async Task Normaliza_la_placa_a_mayusculas_sin_espacios_ni_guiones()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();
        var respuesta = await _cliente.PostearAsync("/api/vehiculos", Vehiculo(Guid.NewGuid(), $"q{sufijo.ToLowerInvariant()} - 12"));

        Assert.Equal(HttpStatusCode.Created, respuesta.Estado);
        Assert.Equal($"Q{sufijo}12", respuesta.Texto("placa"));
    }

    [Fact]
    public async Task La_placa_es_unica_en_toda_la_plataforma()
    {
        var placa = PlacaNueva();
        var primero = await _cliente.PostearAsync("/api/vehiculos", Vehiculo(Guid.NewGuid(), placa));
        var otroTransportista = await _cliente.PostearAsync("/api/vehiculos", Vehiculo(Guid.NewGuid(), placa));

        Assert.Equal(HttpStatusCode.Created, primero.Estado);
        Assert.Equal(HttpStatusCode.Conflict, otroTransportista.Estado);
    }

    [Fact]
    public async Task La_licencia_es_unica_solo_dentro_del_mismo_transportista()
    {
        var transportista = Guid.NewGuid();
        var licencia = LicenciaNueva();

        var primero = await _cliente.PostearAsync("/api/conductores", Conductor(transportista, licencia));
        var mismoTransportista = await _cliente.PostearAsync("/api/conductores", Conductor(transportista, licencia));
        var otroTransportista = await _cliente.PostearAsync("/api/conductores", Conductor(Guid.NewGuid(), licencia));

        Assert.Equal(HttpStatusCode.Created, primero.Estado);
        Assert.Equal(HttpStatusCode.Conflict, mismoTransportista.Estado);
        Assert.Equal(HttpStatusCode.Created, otroTransportista.Estado);
    }

    [Fact]
    public async Task Rechaza_una_capacidad_fuera_de_rango()
    {
        var respuesta = await _cliente.PostearAsync("/api/vehiculos", Vehiculo(Guid.NewGuid(), PlacaNueva(), capacidadKg: 0));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Contains("capacidad", respuesta.Texto("detail"));
    }

    [Fact]
    public async Task Exige_el_usuario_que_registra()
    {
        var respuesta = await _cliente.PostearAsync("/api/vehiculos",
            new { tenantId = Guid.NewGuid(), placa = PlacaNueva(), capacidadKg = 1000 });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.True(respuesta.Propiedad("errors").TryGetProperty("RegistradoPorUserId", out _));
    }

    [Fact]
    public async Task Lista_solo_la_flota_del_transportista_pedido()
    {
        var miTransportista = Guid.NewGuid();
        await _cliente.PostearAsync("/api/vehiculos", Vehiculo(miTransportista, PlacaNueva()));
        await _cliente.PostearAsync("/api/vehiculos", Vehiculo(miTransportista, PlacaNueva()));
        await _cliente.PostearAsync("/api/vehiculos", Vehiculo(Guid.NewGuid(), PlacaNueva()));

        var lista = await _cliente.ConsultarAsync($"/api/vehiculos?tenantId={miTransportista}");

        Assert.Equal(2, lista.Cantidad);
        Assert.All(lista.Json.EnumerateArray(), v => Assert.Equal(miTransportista, v.GetProperty("tenantId").GetGuid()));
    }

    [Fact]
    public async Task El_endpoint_de_contexto_declara_las_referencias_a_identity()
    {
        var contexto = await _cliente.ConsultarAsync("/api/_meta/contexto");

        var conductor = contexto.Propiedad("entidades").EnumerateArray()
            .Single(e => e.GetProperty("nombre").GetString() == "Conductor");
        var referencias = conductor.GetProperty("referenciasExternas").EnumerateArray().ToArray();

        Assert.Contains(referencias, r => r.GetProperty("campo").GetString() == "TenantId"
                                          && r.GetProperty("contexto").GetString() == "Identity");
        Assert.Empty(conductor.GetProperty("relacionesInternas").EnumerateArray());
    }

    [Fact]
    public async Task La_carga_reservada_se_declara_como_referencia_a_cargo_tracking()
    {
        var contexto = await _cliente.ConsultarAsync("/api/_meta/contexto");

        var vehiculo = contexto.Propiedad("entidades").EnumerateArray()
            .Single(e => e.GetProperty("nombre").GetString() == "Vehiculo");
        var referencias = vehiculo.GetProperty("referenciasExternas").EnumerateArray().ToArray();

        Assert.Contains(referencias, r => r.GetProperty("campo").GetString() == "ReservadoParaCargaId"
                                          && r.GetProperty("contexto").GetString() == "CargoTracking");
    }

    // ---- Trabajo 2: reserva y liberacion de recursos (paso 3 del Saga del ADR 0003) ----

    [Fact]
    public async Task Reservar_devuelve_201_la_primera_vez_y_200_si_se_repite()
    {
        var (vehiculoId, conductorId) = await CrearFlotaAsync();
        var cargaId = Guid.NewGuid();

        var primera = await _cliente.PostearAsync("/api/reservas", Asignacion(cargaId, vehiculoId, conductorId));
        var repetida = await _cliente.PostearAsync("/api/reservas", Asignacion(cargaId, vehiculoId, conductorId));

        Assert.Equal(HttpStatusCode.Created, primera.Estado);
        Assert.True(CambioEstado(primera));
        Assert.Equal(HttpStatusCode.OK, repetida.Estado);
        Assert.False(CambioEstado(repetida));
    }

    [Fact]
    public async Task Liberar_devuelve_200_siempre_y_repetirlo_no_cambia_nada()
    {
        var (vehiculoId, conductorId) = await CrearFlotaAsync();
        var cargaId = Guid.NewGuid();
        await _cliente.PostearAsync("/api/reservas", Asignacion(cargaId, vehiculoId, conductorId));

        var primera = await _cliente.PostearAsync("/api/liberaciones", Asignacion(cargaId, vehiculoId, conductorId));
        var repetida = await _cliente.PostearAsync("/api/liberaciones", Asignacion(cargaId, vehiculoId, conductorId));

        Assert.Equal(HttpStatusCode.OK, primera.Estado);
        Assert.True(CambioEstado(primera));
        Assert.Equal("Disponible", primera.Texto("estadoVehiculo"));
        Assert.Equal(HttpStatusCode.OK, repetida.Estado);
        Assert.False(CambioEstado(repetida));
    }

    [Fact]
    public async Task Reservar_un_vehiculo_tomado_por_otra_carga_no_deja_al_conductor_a_medias()
    {
        var transportista = Guid.NewGuid();
        var (vehiculoId, conductorId) = await CrearFlotaAsync(transportista);
        await _cliente.PostearAsync("/api/reservas", Asignacion(Guid.NewGuid(), vehiculoId, conductorId));

        var otroConductor = await _cliente.PostearAsync("/api/conductores", Conductor(transportista, LicenciaNueva()));
        var segundaCarga = await _cliente.PostearAsync("/api/reservas",
            Asignacion(Guid.NewGuid(), vehiculoId, otroConductor.Id("conductorId")));

        var conductorLibre = await _cliente.ConsultarAsync($"/api/conductores/{otroConductor.Id("conductorId")}");

        Assert.Equal(HttpStatusCode.Conflict, segundaCarga.Estado);
        Assert.Equal("Disponible", conductorLibre.Texto("estado"));
    }

    [Fact]
    public async Task Liberar_recursos_de_otra_carga_devuelve_409_y_no_cambia_nada()
    {
        var (vehiculoId, conductorId) = await CrearFlotaAsync();
        var cargaReal = Guid.NewGuid();
        await _cliente.PostearAsync("/api/reservas", Asignacion(cargaReal, vehiculoId, conductorId));

        var otraCarga = await _cliente.PostearAsync("/api/liberaciones",
            Asignacion(Guid.NewGuid(), vehiculoId, conductorId));
        var vehiculo = await _cliente.ConsultarAsync($"/api/vehiculos/{vehiculoId}");

        Assert.Equal(HttpStatusCode.Conflict, otraCarga.Estado);
        Assert.Equal("Reservado", vehiculo.Texto("estado"));
        Assert.Equal(cargaReal, vehiculo.Id("reservadoParaCargaId"));
    }

    [Fact]
    public async Task Rechaza_un_vehiculo_y_un_conductor_de_transportistas_distintos()
    {
        var vehiculo = await _cliente.PostearAsync("/api/vehiculos", Vehiculo(Guid.NewGuid(), PlacaNueva()));
        var conductor = await _cliente.PostearAsync("/api/conductores", Conductor(Guid.NewGuid(), LicenciaNueva()));

        var respuesta = await _cliente.PostearAsync("/api/reservas",
            Asignacion(Guid.NewGuid(), vehiculo.Id("vehiculoId"), conductor.Id("conductorId")));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
    }

    [Fact]
    public async Task Devuelve_404_si_el_vehiculo_no_existe()
    {
        var (_, conductorId) = await CrearFlotaAsync();

        var respuesta = await _cliente.PostearAsync("/api/reservas",
            Asignacion(Guid.NewGuid(), Guid.NewGuid(), conductorId));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.Estado);
    }

    [Fact]
    public async Task Dos_reservas_simultaneas_del_mismo_vehiculo_no_ganan_las_dos()
    {
        var (vehiculoId, conductorId) = await CrearFlotaAsync();

        var primera = _cliente.PostearAsync("/api/reservas", Asignacion(Guid.NewGuid(), vehiculoId, conductorId));
        var segunda = _cliente.PostearAsync("/api/reservas", Asignacion(Guid.NewGuid(), vehiculoId, conductorId));
        var respuestas = await Task.WhenAll(primera, segunda);

        Assert.Single(respuestas, r => r.Estado == HttpStatusCode.Created);
        Assert.Single(respuestas, r => r.Estado == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task El_vehiculo_muestra_su_estado_y_la_carga_para_la_que_esta_reservado()
    {
        var (vehiculoId, conductorId) = await CrearFlotaAsync();
        var cargaId = Guid.NewGuid();

        var recienCreado = await _cliente.ConsultarAsync($"/api/vehiculos/{vehiculoId}");
        await _cliente.PostearAsync("/api/reservas", Asignacion(cargaId, vehiculoId, conductorId));
        var reservado = await _cliente.ConsultarAsync($"/api/vehiculos/{vehiculoId}");

        Assert.Equal("Disponible", recienCreado.Texto("estado"));
        Assert.Equal("Reservado", reservado.Texto("estado"));
        Assert.Equal(cargaId, reservado.Id("reservadoParaCargaId"));
    }

    [Fact]
    public async Task Devuelve_el_correlation_id_recibido_y_genera_uno_cuando_no_viene()
    {
        var conEncabezado = new HttpRequestMessage(HttpMethod.Get, "/api/vehiculos");
        conEncabezado.Headers.Add("X-Correlation-Id", "saga-de-prueba");

        var conocido = await _cliente.SendAsync(conEncabezado);
        var sinEncabezado = await _cliente.GetAsync("/api/vehiculos");

        Assert.Equal("saga-de-prueba", conocido.Headers.GetValues("X-Correlation-Id").Single());
        Assert.NotEmpty(sinEncabezado.Headers.GetValues("X-Correlation-Id").Single());
    }

    // ---- Modo de fallo simulado ----

    [Fact]
    public async Task Las_rutas_de_simulacion_no_existen_con_la_configuracion_por_defecto()
    {
        var consulta = await _cliente.ConsultarAsync("/api/_simulacion/fallos");
        var armado = await _cliente.PostearAsync("/api/_simulacion/fallos",
            new { operacion = "liberaciones", cantidad = 1 });

        Assert.Equal(HttpStatusCode.NotFound, consulta.Estado);
        Assert.Equal(HttpStatusCode.NotFound, armado.Estado);
    }

    [Fact]
    public async Task Con_fallos_armados_las_liberaciones_responden_503_y_luego_vuelven_a_200()
    {
        var cliente = _fabrica
            .WithWebHostBuilder(constructor => constructor.UseSetting("Simulacion:Habilitada", "true"))
            .CreateClient();

        var (vehiculoId, conductorId) = await CrearFlotaAsync();
        var cargaId = Guid.NewGuid();
        await cliente.PostearAsync("/api/reservas", Asignacion(cargaId, vehiculoId, conductorId));
        await cliente.PostearAsync("/api/_simulacion/fallos", new { operacion = "liberaciones", cantidad = 2 });

        var primera = await cliente.PostearAsync("/api/liberaciones", Asignacion(cargaId, vehiculoId, conductorId));
        var segunda = await cliente.PostearAsync("/api/liberaciones", Asignacion(cargaId, vehiculoId, conductorId));
        var tercera = await cliente.PostearAsync("/api/liberaciones", Asignacion(cargaId, vehiculoId, conductorId));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, primera.Estado);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, segunda.Estado);
        Assert.Equal(HttpStatusCode.OK, tercera.Estado);
        Assert.True(CambioEstado(tercera));
    }

    [Fact]
    public async Task Rechaza_armar_una_operacion_que_no_se_puede_hacer_fallar()
    {
        var cliente = _fabrica
            .WithWebHostBuilder(constructor => constructor.UseSetting("Simulacion:Habilitada", "true"))
            .CreateClient();

        var respuesta = await cliente.PostearAsync("/api/_simulacion/fallos",
            new { operacion = "reservas", cantidad = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
    }
}
