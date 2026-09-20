using System.Net;
using EcoTrace.Referencia.Tests.TestSupport;

namespace EcoTrace.Referencia.Tests;

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
        var identity = _identity.CreateClient();
        var fleet = _fleet.CreateClient();
        var cargo = _cargo.CreateClient();
        var billing = _billing.CreateClient();

        // 1. Identity emite los identificadores base.
        var generador = await identity.PostearAsync("/api/tenants", new { nombre = "Generadora del Valle S.A.", tenantType = "Generador" });
        var transportista = await identity.PostearAsync("/api/tenants", new { nombre = "Transportes del Cauca", tenantType = "Transportista" });
        var supervisor = await identity.PostearAsync("/api/users", new
        {
            tenantId = transportista.Id("tenantId"),
            role = "Supervisor",
            nombre = "Marta Ríos",
            email = $"marta.{Guid.NewGuid():N}@transportescauca.co"
        });
        var generadorId = generador.Id("tenantId");
        var transportistaId = transportista.Id("tenantId");
        var supervisorId = supervisor.Id("userId");

        // Identity deja de existir. Los otros tres contextos siguen operando: no dependen de el
        // en tiempo de ejecucion, solo guardan los identificadores que recibieron.
        await _identity.DisposeAsync();

        // 2. Fleet Management registra vehiculo y conductor del transportista.
        var vehiculo = await fleet.PostearAsync("/api/vehiculos", new
        {
            tenantId = transportistaId, registradoPorUserId = supervisorId, placa = "WXY123", capacidadKg = 5000
        });
        var conductor = await fleet.PostearAsync("/api/conductores", new
        {
            tenantId = transportistaId, registradoPorUserId = supervisorId, nombre = "Carlos Ruiz", licencia = "C2-88213"
        });
        Assert.Equal(HttpStatusCode.Created, vehiculo.Estado);
        Assert.Equal(HttpStatusCode.Created, conductor.Estado);

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
