using System.Net;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

public sealed class BillingApiTests(ApiFactory<BillingApiMarker> fabrica) : IClassFixture<ApiFactory<BillingApiMarker>>
{
    private readonly HttpClient _cliente = fabrica.CreateClient();

    // Billing tampoco consulta a Cargo & Tracking: cualquier CargaId sirve para crear el pago.
    private static object NuevoPago(Guid? cargaId = null, decimal monto = 850_000m) => new
    {
        generadorTenantId = Guid.NewGuid(),
        transportistaTenantId = Guid.NewGuid(),
        cargaId = cargaId ?? Guid.NewGuid(),
        monto
    };

    private async Task<Guid> CrearPagoAsync()
    {
        var pago = await _cliente.PostearAsync("/api/pagos", NuevoPago());
        Assert.Equal(HttpStatusCode.Created, pago.Estado);
        return pago.Propiedad("pago").GetProperty("pagoId").GetGuid();
    }

    [Fact]
    public async Task Un_pago_nuevo_queda_en_custodia_con_su_primera_auditoria()
    {
        var pago = await _cliente.PostearAsync("/api/pagos", NuevoPago());

        Assert.Equal(HttpStatusCode.Created, pago.Estado);
        Assert.Equal("EnCustodia", pago.Propiedad("pago").GetProperty("estadoEscrow").GetString());
        Assert.Equal("COP", pago.Propiedad("pago").GetProperty("moneda").GetString());
        Assert.Equal(1, pago.Propiedad("auditoria").GetArrayLength());
    }

    [Fact]
    public async Task Liberar_deja_el_historial_completo_en_orden()
    {
        var pagoId = await CrearPagoAsync();

        var liberado = await _cliente.PostearAsync($"/api/pagos/{pagoId}/liberar");
        var auditoria = await _cliente.ConsultarAsync($"/api/pagos/{pagoId}/auditoria");

        Assert.Equal(HttpStatusCode.OK, liberado.Estado);
        Assert.Equal("Liberado", liberado.Propiedad("pago").GetProperty("estadoEscrow").GetString());
        var estados = auditoria.Json.EnumerateArray().Select(a => a.GetProperty("estadoResultante").GetString()!).ToArray();
        Assert.Equal(new[] { "EnCustodia", "Liberado" }, estados);
    }

    [Fact]
    public async Task Un_pago_liberado_no_se_puede_liberar_ni_reembolsar()
    {
        var pagoId = await CrearPagoAsync();
        await _cliente.PostearAsync($"/api/pagos/{pagoId}/liberar");

        var otraVez = await _cliente.PostearAsync($"/api/pagos/{pagoId}/liberar");
        var reembolso = await _cliente.PostearAsync($"/api/pagos/{pagoId}/reembolso");

        Assert.Equal(HttpStatusCode.Conflict, otraVez.Estado);
        Assert.Equal(HttpStatusCode.Conflict, reembolso.Estado);
    }

    [Fact]
    public async Task Reembolsar_devuelve_los_fondos_y_cierra_el_pago()
    {
        var pagoId = await CrearPagoAsync();

        var reembolso = await _cliente.PostearAsync($"/api/pagos/{pagoId}/reembolso");
        var liberarDespues = await _cliente.PostearAsync($"/api/pagos/{pagoId}/liberar");

        Assert.Equal("Reembolsado", reembolso.Propiedad("pago").GetProperty("estadoEscrow").GetString());
        Assert.Equal(HttpStatusCode.Conflict, liberarDespues.Estado);
    }

    [Fact]
    public async Task Una_carga_admite_un_solo_pago_en_escrow()
    {
        var cargaId = Guid.NewGuid();

        var primero = await _cliente.PostearAsync("/api/pagos", NuevoPago(cargaId));
        var segundo = await _cliente.PostearAsync("/api/pagos", NuevoPago(cargaId));

        Assert.Equal(HttpStatusCode.Created, primero.Estado);
        Assert.Equal(HttpStatusCode.Conflict, segundo.Estado);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    [InlineData(1000.123)]
    public async Task Rechaza_montos_no_validos(double monto)
    {
        var respuesta = await _cliente.PostearAsync("/api/pagos", NuevoPago(monto: (decimal)monto));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
    }

    [Fact]
    public async Task Dos_liberaciones_simultaneas_nunca_mueven_el_dinero_dos_veces()
    {
        var pagoId = await CrearPagoAsync();

        var resultados = await Task.WhenAll(
            _cliente.PostearAsync($"/api/pagos/{pagoId}/liberar"),
            _cliente.PostearAsync($"/api/pagos/{pagoId}/liberar"));

        Assert.Equal(1, resultados.Count(r => r.Estado == HttpStatusCode.OK));
        Assert.Equal(1, resultados.Count(r => r.Estado == HttpStatusCode.Conflict));

        var auditoria = await _cliente.ConsultarAsync($"/api/pagos/{pagoId}/auditoria");
        Assert.Equal(2, auditoria.Cantidad);
    }

    [Fact]
    public async Task Emite_una_factura_con_numero_y_solo_una_por_carga()
    {
        var cargaId = Guid.NewGuid();
        object Factura() => new
        {
            generadorTenantId = Guid.NewGuid(),
            transportistaTenantId = Guid.NewGuid(),
            cargaId,
            monto = 850_000m
        };

        var primera = await _cliente.PostearAsync("/api/facturas", Factura());
        var segunda = await _cliente.PostearAsync("/api/facturas", Factura());

        Assert.Equal(HttpStatusCode.Created, primera.Estado);
        Assert.StartsWith("FAC-", primera.Texto("numero"));
        Assert.Equal(HttpStatusCode.Conflict, segunda.Estado);
    }

    [Fact]
    public async Task El_endpoint_de_contexto_declara_las_referencias_del_pago()
    {
        var contexto = await _cliente.ConsultarAsync("/api/_meta/contexto");
        var pago = contexto.Propiedad("entidades").EnumerateArray()
            .Single(e => e.GetProperty("nombre").GetString() == "Pago");

        var destinos = pago.GetProperty("referenciasExternas").EnumerateArray()
            .ToDictionary(r => r.GetProperty("campo").GetString()!, r => r.GetProperty("contexto").GetString());

        Assert.Equal("Identity", destinos["GeneradorTenantId"]);
        Assert.Equal("Identity", destinos["TransportistaTenantId"]);
        Assert.Equal("CargoTracking", destinos["CargaId"]);
    }
}
