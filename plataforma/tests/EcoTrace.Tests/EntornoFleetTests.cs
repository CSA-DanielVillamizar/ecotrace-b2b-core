using System.Net;
using EcoTrace.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;

namespace EcoTrace.Tests;

/// <summary>
/// Lo que usa la verificación del release de Fleet (ADR 0006): el servicio dice en qué entorno corre, solo
/// producción tiene el vehículo conocido, y un token de un entorno no sirve en el otro porque cambia el emisor.
/// </summary>
public sealed class EntornoFleetTests
{
    private const string EmisorProduccion = "https://identity.ecotrace.local/produccion";
    private static readonly Guid VehiculoConocido = Guid.Parse("f1ee7000-0000-4000-8000-000000000001");

    [Fact]
    public async Task En_desarrollo_dice_su_entorno_y_no_tiene_el_vehiculo_conocido()
    {
        using var fleet = new FleetConIdentity();

        var entorno = await fleet.ClienteFleet().ConsultarAsync("/api/_meta/entorno");
        using var servicio = fleet.ClienteFleet(FleetConIdentity.TokenDeServicio());
        var vehiculo = await servicio.ConsultarAsync($"/api/vehiculos/{VehiculoConocido}");

        Assert.Equal("FleetManagement", entorno.Texto("servicio"));
        Assert.Equal("desarrollo", entorno.Texto("entorno"));
        Assert.False(string.IsNullOrWhiteSpace(entorno.Texto("version")));
        Assert.Equal(HttpStatusCode.NotFound, vehiculo.Estado);
    }

    [Fact]
    public async Task En_produccion_dice_su_entorno_y_tiene_el_vehiculo_conocido()
    {
        using var fleet = new FleetConIdentity();
        using var produccion = fleet.Fleet.WithWebHostBuilder(b => b.UseEnvironment("Production"));

        var entorno = await produccion.CreateClient().ConsultarAsync("/api/_meta/entorno");
        using var servicio = produccion.CreateClient().ConToken(
            Acceso.Forjar(rol: "Servicio", emisor: EmisorProduccion, audiencias: ["ecotrace-fleet"]));
        var vehiculo = await servicio.ConsultarAsync($"/api/vehiculos/{VehiculoConocido}");

        Assert.Equal("produccion", entorno.Texto("entorno"));
        Assert.Equal(HttpStatusCode.OK, vehiculo.Estado);
        Assert.Equal("PRD001", vehiculo.Texto("placa"));
    }

    [Fact]
    public async Task Un_token_de_desarrollo_no_sirve_en_produccion()
    {
        using var fleet = new FleetConIdentity();
        using var produccion = fleet.Fleet.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        using var conTokenDeDesarrollo = produccion.CreateClient().ConToken(FleetConIdentity.TokenDeServicio());

        var respuesta = await conTokenDeDesarrollo.ConsultarAsync($"/api/vehiculos/{VehiculoConocido}");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.Estado);
    }
}
