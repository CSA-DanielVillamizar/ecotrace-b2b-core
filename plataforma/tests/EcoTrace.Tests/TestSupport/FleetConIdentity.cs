using EcoTrace.FleetManagement.Api.Seguridad;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EcoTrace.Tests.TestSupport;

/// <summary>
/// Fleet Management en memoria junto a un Identity real. Fleet descarga el JWKS de ese Identity por un cable
/// que la prueba controla: puede contar cuántas veces lo pidió o cortarlo para simular que Identity se cayó.
/// </summary>
public sealed class FleetConIdentity : IDisposable
{
    private readonly ApiFactory<IdentityApiMarker> _identity = new();
    private readonly ApiFactory<FleetManagementApiMarker> _fleetBase = new();

    public FleetConIdentity()
    {
        JwksDesdeFleet = new EnlaceDeRed(() => _identity.Server.CreateHandler());
        Fleet = _fleetBase.WithWebHostBuilder(b => b.ConfigureTestServices(servicios =>
            servicios.AddHttpClient(SeguridadFleet.ClienteJwks).ConfigurePrimaryHttpMessageHandler(() => JwksDesdeFleet)));
    }

    public WebApplicationFactory<FleetManagementApiMarker> Fleet { get; }

    /// <summary>Cable de Fleet hacia el JWKS de Identity.</summary>
    public EnlaceDeRed JwksDesdeFleet { get; }

    public HttpClient ClienteIdentity() => _identity.CreateClient();

    public HttpClient ClienteFleet(string? token = null)
    {
        var cliente = Fleet.CreateClient();
        return token is null ? cliente : cliente.ConToken(token);
    }

    /// <summary>Token firmado con la clave de Identity, para Fleet, con la organización y el rol dados.</summary>
    public static string Token(Guid tenantId, string rol = "Supervisor") =>
        Acceso.Forjar(tenantId, rol, audiencias: ["ecotrace-fleet"]);

    /// <summary>Token como el que Identity le da a un servicio: <c>role = Servicio</c> y sin organización.</summary>
    public static string TokenDeServicio() =>
        Acceso.Forjar(rol: "Servicio", audiencias: ["ecotrace-fleet"]);

    public void Dispose()
    {
        _fleetBase.Dispose();
        _identity.Dispose();
    }
}
