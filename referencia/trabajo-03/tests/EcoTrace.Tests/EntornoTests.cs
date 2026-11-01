using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EcoTrace.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EcoTrace.Tests;

/// <summary>
/// <c>GET /api/_meta/entorno</c>: cada servicio dice en qué entorno corre. Es lo que la Capa 2 de la verificación de release
/// (scripts/verificar-release.ps1) pregunta para saber que la app habla con producción de verdad y no con desarrollo.
/// </summary>
public sealed class EntornoTests
{
    private static async Task<JsonElement> LeerAsync<T>(WebApplicationFactory<T> servicio)
        where T : class
    {
        var respuesta = await servicio.CreateClient().GetAsync("/api/_meta/entorno");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return await respuesta.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Cada_servicio_dice_su_nombre_su_entorno_y_su_version()
    {
        await using var identity = new ApiFactory<IdentityApiMarker>();
        await using var fleet = new ApiFactory<FleetManagementApiMarker>();
        await using var cargo = new ApiFactory<CargoTrackingApiMarker>();
        await using var billing = new ApiFactory<BillingApiMarker>();

        foreach (var cuerpo in new[] { await LeerAsync(identity), await LeerAsync(fleet), await LeerAsync(cargo), await LeerAsync(billing) })
        {
            Assert.False(string.IsNullOrWhiteSpace(cuerpo.GetProperty("servicio").GetString()));
            Assert.Equal("desarrollo", cuerpo.GetProperty("entorno").GetString());
            Assert.Matches(@"^\d+\.\d+\.\d+$", cuerpo.GetProperty("version").GetString()!);
        }
    }

    [Fact]
    public async Task Un_servicio_en_produccion_responde_produccion()
    {
        await using var fleetBase = new ApiFactory<FleetManagementApiMarker>();
        using var fleet = fleetBase.WithWebHostBuilder(b => b.UseEnvironment("Production"));

        var cuerpo = await LeerAsync(fleet);

        Assert.Equal("produccion", cuerpo.GetProperty("entorno").GetString());
    }

    [Fact]
    public async Task No_expone_secretos()
    {
        await using var identity = new ApiFactory<IdentityApiMarker>();

        var texto = await (await identity.CreateClient().GetAsync("/api/_meta/entorno")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("PRIVATE", texto);
        Assert.DoesNotContain("secret", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", texto, StringComparison.OrdinalIgnoreCase);
    }
}
