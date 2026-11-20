using System.Net;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

public sealed class FleetManagementApiTests(FleetConIdentity fleet) : IClassFixture<FleetConIdentity>
{
    // Desde el Trabajo 3 toda ruta de Fleet exige un JWT de Identity. Cada prueba es un transportista nuevo:
    // la organización sale del token, así que basta con un TenantId distinto en cada una.
    private readonly Guid _transportista = Guid.NewGuid();

    // Fleet Management no consulta a Identity: los TenantId y UserId de estas pruebas son Guid
    // inventados. Que el servicio los acepte es la prueba de que solo guarda el identificador.
    private HttpClient ClienteDe(Guid tenantId) => fleet.ClienteFleet(FleetConIdentity.Token(tenantId));

    private HttpClient? _cliente;

    private HttpClient Cliente => _cliente ??= ClienteDe(_transportista);

    private static object Vehiculo(string placa, int capacidadKg = 5000) => new { placa, capacidadKg };

    private static object Conductor(string licencia) => new { nombre = "Carlos Ruiz", licencia };

    private static string PlacaNueva() => "T" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    [Fact]
    public async Task Toma_la_organizacion_y_quien_registra_del_token_y_no_del_cuerpo()
    {
        var token = FleetConIdentity.Token(_transportista);
        var sub = Acceso.Payload(token).GetProperty("sub").GetGuid();
        using var cliente = fleet.ClienteFleet(token);

        // Aunque el cuerpo diga otra organización, Fleet usa la del token.
        var respuesta = await cliente.PostearAsync("/api/vehiculos", new
        {
            tenantId = Guid.NewGuid(), registradoPorUserId = Guid.NewGuid(), placa = PlacaNueva(), capacidadKg = 5000
        });

        Assert.Equal(HttpStatusCode.Created, respuesta.Estado);
        Assert.Equal(_transportista, respuesta.Id("tenantId"));
        Assert.Equal(sub, respuesta.Id("registradoPorUserId"));
    }

    [Fact]
    public async Task Normaliza_la_placa_a_mayusculas_sin_espacios_ni_guiones()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..3].ToUpperInvariant();
        var respuesta = await Cliente.PostearAsync("/api/vehiculos", Vehiculo($"q{sufijo.ToLowerInvariant()} - 12"));

        Assert.Equal(HttpStatusCode.Created, respuesta.Estado);
        Assert.Equal($"Q{sufijo}12", respuesta.Texto("placa"));
    }

    [Fact]
    public async Task La_placa_es_unica_en_toda_la_plataforma()
    {
        var placa = PlacaNueva();
        var primero = await Cliente.PostearAsync("/api/vehiculos", Vehiculo(placa));
        var otroTransportista = await ClienteDe(Guid.NewGuid()).PostearAsync("/api/vehiculos", Vehiculo(placa));

        Assert.Equal(HttpStatusCode.Created, primero.Estado);
        Assert.Equal(HttpStatusCode.Conflict, otroTransportista.Estado);
    }

    [Fact]
    public async Task La_licencia_es_unica_solo_dentro_del_mismo_transportista()
    {
        var licencia = "LIC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

        var primero = await Cliente.PostearAsync("/api/conductores", Conductor(licencia));
        var mismoTransportista = await Cliente.PostearAsync("/api/conductores", Conductor(licencia));
        var otroTransportista = await ClienteDe(Guid.NewGuid()).PostearAsync("/api/conductores", Conductor(licencia));

        Assert.Equal(HttpStatusCode.Created, primero.Estado);
        Assert.Equal(HttpStatusCode.Conflict, mismoTransportista.Estado);
        Assert.Equal(HttpStatusCode.Created, otroTransportista.Estado);
    }

    [Fact]
    public async Task Rechaza_una_capacidad_fuera_de_rango()
    {
        var respuesta = await Cliente.PostearAsync("/api/vehiculos", Vehiculo(PlacaNueva(), capacidadKg: 0));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Contains("capacidad", respuesta.Texto("detail"));
    }

    [Fact]
    public async Task Exige_la_placa()
    {
        var respuesta = await Cliente.PostearAsync("/api/vehiculos", new { capacidadKg = 1000 });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.True(respuesta.Propiedad("errors").TryGetProperty("Placa", out _));
    }

    [Fact]
    public async Task Lista_solo_la_flota_de_la_organizacion_del_token()
    {
        await Cliente.PostearAsync("/api/vehiculos", Vehiculo(PlacaNueva()));
        await Cliente.PostearAsync("/api/vehiculos", Vehiculo(PlacaNueva()));
        await ClienteDe(Guid.NewGuid()).PostearAsync("/api/vehiculos", Vehiculo(PlacaNueva()));

        var lista = await Cliente.ConsultarAsync("/api/vehiculos");

        Assert.Equal(2, lista.Cantidad);
        Assert.All(lista.Json.EnumerateArray(), v => Assert.Equal(_transportista, v.GetProperty("tenantId").GetGuid()));
    }

    [Fact]
    public async Task El_endpoint_de_contexto_declara_las_referencias_a_identity()
    {
        // Es metadata del modelo, sin datos de nadie: no pide token.
        var contexto = await fleet.ClienteFleet().ConsultarAsync("/api/_meta/contexto");

        var conductor = contexto.Propiedad("entidades").EnumerateArray()
            .Single(e => e.GetProperty("nombre").GetString() == "Conductor");
        var referencias = conductor.GetProperty("referenciasExternas").EnumerateArray().ToArray();

        Assert.Contains(referencias, r => r.GetProperty("campo").GetString() == "TenantId"
                                          && r.GetProperty("contexto").GetString() == "Identity");
        Assert.Empty(conductor.GetProperty("relacionesInternas").EnumerateArray());
    }
}
