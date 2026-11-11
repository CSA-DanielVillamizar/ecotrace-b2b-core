using System.Net;
using EcoTrace.Billing.Api.Seguridad;
using EcoTrace.FleetManagement.Api.Seguridad;
using EcoTrace.Tests.TestSupport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace EcoTrace.Tests;

/// <summary>
/// El ciclo de negocio completo de EcoTrace atravesando los cuatro contextos. Entre un
/// servicio y otro solo viajan identificadores, copiados a mano por el cliente: ningun modulo
/// llama a otro ni lee su base de datos (Trabajo 2 agrega la comunicacion real entre servicios).
/// </summary>
public sealed class FlujoEntreContextosTests : IAsyncLifetime
{
    private readonly ApiFactory<IdentityApiMarker> _identity = new();
    private readonly ApiFactory<FleetManagementApiMarker> _fleet = new();
    private readonly ApiFactory<CargoTrackingApiMarker> _cargo = new();
    private readonly ApiFactory<BillingApiMarker> _billing = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _identity.DisposeAsync();
        await _fleet.DisposeAsync();
        await _cargo.DisposeAsync();
        await _billing.DisposeAsync();
    }

    [Fact]
    public async Task Del_alta_de_organizaciones_a_la_liberacion_del_pago_solo_con_identificadores()
    {
        var identity = await Acceso.ClienteAdministradorAsync(_identity.CreateClient);
        // Fleet valida el token con la clave pública que publica este Identity (Trabajo 3).
        var fleet = _fleet.WithWebHostBuilder(b => b.ConfigureTestServices(servicios =>
                servicios.AddHttpClient(SeguridadFleet.ClienteJwks).ConfigurePrimaryHttpMessageHandler(() => _identity.Server.CreateHandler())))
            .CreateClient();
        // Cargo exige un token: este cliente llama como la persona correcta para cada ruta (ver ActorAutomatico).
        var cargo = _cargo.CreateDefaultClient(new ActorAutomatico());
        // Billing también valida el token con la clave pública de este Identity (Trabajo 3).
        var billing = _billing.WithWebHostBuilder(b => b.ConfigureTestServices(servicios =>
                servicios.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                    o.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        "http://localhost/.well-known/jwks.json",
                        new JwksBillingRetriever(),
                        new HttpDocumentRetriever(new HttpClient(_identity.Server.CreateHandler())) { RequireHttps = false }))))
            .CreateClient();

        // 1. Identity emite los identificadores base.
        var generador = await identity.PostearAsync("/api/tenants", new { nombre = "Generadora del Valle S.A.", tenantType = "Generador" });
        var transportista = await identity.PostearAsync("/api/tenants", new { nombre = "Transportes del Cauca", tenantType = "Transportista" });
        var emailSupervisor = $"marta.{Guid.NewGuid():N}@transportescauca.co";
        await identity.PostearAsync("/api/users", new
        {
            tenantId = transportista.Id("tenantId"),
            role = "Supervisor",
            nombre = "Marta Ríos",
            email = emailSupervisor,
            password = "Supervisor-2026"
        });
        var generadorId = generador.Id("tenantId");
        var transportistaId = transportista.Id("tenantId");

        // La supervisora inicia sesión y Fleet descarga la clave pública de Identity con su primera solicitud.
        fleet.ConToken(await identity.TokenDeAsync(emailSupervisor, "Supervisor-2026"));
        Assert.Equal(HttpStatusCode.OK, (await fleet.ConsultarAsync("/api/vehiculos")).Estado);

        // Lo mismo con Billing, con el token del administrador de la plataforma.
        billing.ConToken(await _identity.CreateClient().TokenAdministradorAsync());
        Assert.Equal(HttpStatusCode.OK, (await billing.ConsultarAsync("/api/pagos")).Estado);

        // Identity deja de existir. Los otros tres contextos siguen operando: solo guardan los identificadores
        // que recibieron, y Fleet y Billing validan el token con la clave que ya tienen en memoria, sin volver a Identity.
        await _identity.DisposeAsync();

        // 2. Fleet Management registra vehiculo y conductor del transportista. La organización y quién
        //    registra salen del token de la supervisora.
        var vehiculo = await fleet.PostearAsync("/api/vehiculos", new { placa = "WXY123", capacidadKg = 5000 });
        var conductor = await fleet.PostearAsync("/api/conductores", new { nombre = "Carlos Ruiz", licencia = "C2-88213" });
        Assert.Equal(HttpStatusCode.Created, vehiculo.Estado);
        Assert.Equal(HttpStatusCode.Created, conductor.Estado);
        Assert.Equal(transportistaId, vehiculo.Id("tenantId"));

        // 3. Cargo & Tracking crea la carga con los dos tenants y la asigna con IDs de Fleet.
        var carga = await cargo.PostearAsync("/api/cargas", new
        {
            generadorTenantId = generadorId,
            transportistaTenantId = transportistaId,
            descripcion = "Residuos industriales no peligrosos",
            origen = "Cali",
            destino = "Popayán",
            pesoKg = 4200
        });
        var cargaId = carga.Id("cargaId");
        await cargo.PostearAsync($"/api/cargas/{cargaId}/asignacion",
            new { vehiculoId = vehiculo.Id("vehiculoId"), conductorId = conductor.Id("conductorId") });
        await cargo.PostearAsync($"/api/cargas/{cargaId}/seguimientos", new { estado = "EnTransito", ubicacion = "Santander de Quilichao" });
        var entregada = await cargo.PostearAsync($"/api/cargas/{cargaId}/seguimientos", new { estado = "Entregado", ubicacion = "Popayán" });
        Assert.Equal("Entregado", entregada.Propiedad("carga").GetProperty("estado").GetString());

        // 4. Billing retiene, factura y libera el pago con los mismos identificadores.
        var pago = await billing.PostearAsync("/api/pagos", new
        {
            generadorTenantId = generadorId, transportistaTenantId = transportistaId, cargaId, monto = 850_000m
        });
        await billing.PostearAsync("/api/facturas", new
        {
            generadorTenantId = generadorId, transportistaTenantId = transportistaId, cargaId, monto = 850_000m
        });
        var pagoId = pago.Propiedad("pago").GetProperty("pagoId").GetGuid();
        var liberado = await billing.PostearAsync($"/api/pagos/{pagoId}/liberar");

        Assert.Equal(HttpStatusCode.OK, liberado.Estado);
        Assert.Equal("Liberado", liberado.Propiedad("pago").GetProperty("estadoEscrow").GetString());
        Assert.Equal(cargaId, liberado.Propiedad("pago").GetProperty("cargaId").GetGuid());
    }
}
