using System.Net;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

/// <summary>
/// El módulo de Fleet Management visto solo. Cada transportista es una organización con su supervisor, que es quien
/// registra la flota; el operador de la plataforma solo lee. Las reglas de seguridad tienen sus propias pruebas.
/// </summary>
public sealed class FleetManagementApiTests(ApiFactory<FleetManagementApiMarker> fabrica)
    : IClassFixture<ApiFactory<FleetManagementApiMarker>>
{
    private readonly HttpClient _lector = fabrica.ComoOperador();

    // Fleet Management no consulta a Identity: los TenantId y UserId de estas pruebas son Guid inventados. Que el
    // servicio los acepte es la prueba de que solo guarda el identificador (la organización la dice el token).
    private static Persona Supervisor(Guid? tenantId = null) => new(tenantId ?? Guid.NewGuid(), Rol.Supervisor);

    private static object Vehiculo(Persona quien, string placa, int capacidadKg = 5000) =>
        new { tenantId = quien.TenantId, registradoPorUserId = quien.UserId, placa, capacidadKg };

    private static object Conductor(Persona quien, string licencia) =>
        new { tenantId = quien.TenantId, registradoPorUserId = quien.UserId, nombre = "Carlos Ruiz", licencia };

    private Task<Respuesta> RegistrarVehiculoAsync(Persona quien, string placa, int capacidadKg = 5000) =>
        fabrica.Como(quien).PostearAsync("/api/vehiculos", Vehiculo(quien, placa, capacidadKg));

    private Task<Respuesta> RegistrarConductorAsync(Persona quien, string licencia) =>
        fabrica.Como(quien).PostearAsync("/api/conductores", Conductor(quien, licencia));

    private static string PlacaNueva() => "T" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    [Fact]
    public async Task Guarda_los_identificadores_externos_sin_validarlos_contra_identity()
    {
        var quien = Supervisor();
        var respuesta = await RegistrarVehiculoAsync(quien, PlacaNueva());

        Assert.Equal(HttpStatusCode.Created, respuesta.Estado);
        Assert.Equal(quien.TenantId, respuesta.Id("tenantId"));
    }

    [Fact]
    public async Task Normaliza_la_placa_a_mayusculas_sin_espacios_ni_guiones()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();
        var respuesta = await RegistrarVehiculoAsync(Supervisor(), $"q{sufijo.ToLowerInvariant()} - 12");

        Assert.Equal(HttpStatusCode.Created, respuesta.Estado);
        Assert.Equal($"Q{sufijo}12", respuesta.Texto("placa"));
    }

    [Fact]
    public async Task La_placa_es_unica_en_toda_la_plataforma()
    {
        var placa = PlacaNueva();
        var primero = await RegistrarVehiculoAsync(Supervisor(), placa);
        var otroTransportista = await RegistrarVehiculoAsync(Supervisor(), placa);

        Assert.Equal(HttpStatusCode.Created, primero.Estado);
        Assert.Equal(HttpStatusCode.Conflict, otroTransportista.Estado);
    }

    [Fact]
    public async Task La_licencia_es_unica_solo_dentro_del_mismo_transportista()
    {
        var transportista = Supervisor();
        var licencia = "LIC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var primero = await RegistrarConductorAsync(transportista, licencia);
        var mismoTransportista = await RegistrarConductorAsync(transportista, licencia);
        var otroTransportista = await RegistrarConductorAsync(Supervisor(), licencia);

        Assert.Equal(HttpStatusCode.Created, primero.Estado);
        Assert.Equal(HttpStatusCode.Conflict, mismoTransportista.Estado);
        Assert.Equal(HttpStatusCode.Created, otroTransportista.Estado);
    }

    [Fact]
    public async Task Rechaza_una_capacidad_fuera_de_rango()
    {
        var respuesta = await RegistrarVehiculoAsync(Supervisor(), PlacaNueva(), capacidadKg: 0);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Contains("capacidad", respuesta.Texto("detail"));
    }

    [Fact]
    public async Task Exige_el_usuario_que_registra()
    {
        var quien = Supervisor();
        var respuesta = await fabrica.Como(quien).PostearAsync("/api/vehiculos",
            new { tenantId = quien.TenantId, placa = PlacaNueva(), capacidadKg = 1000 });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.True(respuesta.Propiedad("errors").TryGetProperty("RegistradoPorUserId", out _));
    }

    [Fact]
    public async Task Lista_solo_la_flota_del_transportista_pedido()
    {
        var miTransportista = Supervisor();
        await RegistrarVehiculoAsync(miTransportista, PlacaNueva());
        await RegistrarVehiculoAsync(miTransportista, PlacaNueva());
        await RegistrarVehiculoAsync(Supervisor(), PlacaNueva());

        var lista = await _lector.ConsultarAsync($"/api/vehiculos?tenantId={miTransportista.TenantId}");

        Assert.Equal(2, lista.Cantidad);
        Assert.All(lista.Json.EnumerateArray(), v => Assert.Equal(miTransportista.TenantId, v.GetProperty("tenantId").GetGuid()));
    }

    [Fact]
    public async Task El_endpoint_de_contexto_declara_las_referencias_a_identity()
    {
        var contexto = await _lector.ConsultarAsync("/api/_meta/contexto");

        var conductor = contexto.Propiedad("entidades").EnumerateArray()
            .Single(e => e.GetProperty("nombre").GetString() == "Conductor");
        var referencias = conductor.GetProperty("referenciasExternas").EnumerateArray().ToArray();

        Assert.Contains(referencias, r => r.GetProperty("campo").GetString() == "TenantId"
                                          && r.GetProperty("contexto").GetString() == "Identity");
        Assert.Empty(conductor.GetProperty("relacionesInternas").EnumerateArray());
    }
}
