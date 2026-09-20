using System.Net;
using EcoTrace.Referencia.Tests.TestSupport;

namespace EcoTrace.Referencia.Tests;

public sealed class FleetManagementApiTests(ApiFactory<FleetManagementApiMarker> fabrica)
    : IClassFixture<ApiFactory<FleetManagementApiMarker>>
{
    private readonly HttpClient _cliente = fabrica.CreateClient();

    // Fleet Management no consulta a Identity: los TenantId y UserId de estas pruebas son Guid
    // inventados. Que el servicio los acepte es la prueba de que solo guarda el identificador.
    private static object Vehiculo(Guid tenantId, string placa, int capacidadKg = 5000) =>
        new { tenantId, registradoPorUserId = Guid.NewGuid(), placa, capacidadKg };

    private static object Conductor(Guid tenantId, string licencia) =>
        new { tenantId, registradoPorUserId = Guid.NewGuid(), nombre = "Carlos Ruiz", licencia };

    private static string PlacaNueva() => "T" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

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
        var licencia = "LIC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

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
}
