using System.Net;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

/// <summary>
/// El módulo de Billing &amp; Escrow visto solo. El generador paga y libera, y el operador de la plataforma solo lee.
/// Las reglas de seguridad tienen sus propias pruebas.
/// </summary>
public sealed class BillingApiTests(ApiFactory<BillingApiMarker> fabrica) : IClassFixture<ApiFactory<BillingApiMarker>>
{
    private readonly HttpClient _lector = fabrica.ComoOperador();

    // Billing tampoco consulta a Cargo & Tracking: cualquier CargaId sirve para crear el pago.
    private static object NuevoPago(ParDeOrganizaciones par, Guid? cargaId = null, decimal monto = 850_000m) => new
    {
        generadorTenantId = par.GeneradorId,
        transportistaTenantId = par.TransportistaId,
        cargaId = cargaId ?? Guid.NewGuid(),
        monto
    };

    private async Task<Guid> CrearPagoAsync(ParDeOrganizaciones par)
    {
        var pago = await fabrica.Como(par.AdminGenerador).PostearAsync("/api/pagos", NuevoPago(par));
        Assert.Equal(HttpStatusCode.Created, pago.Estado);
        return pago.Propiedad("pago").GetProperty("pagoId").GetGuid();
    }

    [Fact]
    public async Task Un_pago_nuevo_queda_en_custodia_con_su_primera_auditoria()
    {
        var par = ParDeOrganizaciones.Nuevo();

        var pago = await fabrica.Como(par.AdminGenerador).PostearAsync("/api/pagos", NuevoPago(par));

        Assert.Equal(HttpStatusCode.Created, pago.Estado);
        Assert.Equal("EnCustodia", pago.Propiedad("pago").GetProperty("estadoEscrow").GetString());
        Assert.Equal("COP", pago.Propiedad("pago").GetProperty("moneda").GetString());
        Assert.Equal(1, pago.Propiedad("auditoria").GetArrayLength());
    }

    [Fact]
    public async Task Liberar_deja_el_historial_completo_en_orden()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var pagoId = await CrearPagoAsync(par);

        var liberado = await fabrica.Como(par.AdminGenerador).PostearAsync($"/api/pagos/{pagoId}/liberar");
        var auditoria = await _lector.ConsultarAsync($"/api/pagos/{pagoId}/auditoria");

        Assert.Equal(HttpStatusCode.OK, liberado.Estado);
        Assert.Equal("Liberado", liberado.Propiedad("pago").GetProperty("estadoEscrow").GetString());
        var estados = auditoria.Json.EnumerateArray().Select(a => a.GetProperty("estadoResultante").GetString()!).ToArray();
        Assert.Equal(new[] { "EnCustodia", "Liberado" }, estados);
    }

    [Fact]
    public async Task Un_pago_liberado_no_se_puede_liberar_ni_reembolsar()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var pagoId = await CrearPagoAsync(par);
        var admin = fabrica.Como(par.AdminGenerador);
        await admin.PostearAsync($"/api/pagos/{pagoId}/liberar");

        var otraVez = await admin.PostearAsync($"/api/pagos/{pagoId}/liberar");
        var reembolso = await admin.PostearAsync($"/api/pagos/{pagoId}/reembolso");

        Assert.Equal(HttpStatusCode.Conflict, otraVez.Estado);
        Assert.Equal(HttpStatusCode.Conflict, reembolso.Estado);
    }

    [Fact]
    public async Task Reembolsar_devuelve_los_fondos_y_cierra_el_pago()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var pagoId = await CrearPagoAsync(par);
        var admin = fabrica.Como(par.AdminGenerador);

        var reembolso = await admin.PostearAsync($"/api/pagos/{pagoId}/reembolso");
        var liberarDespues = await admin.PostearAsync($"/api/pagos/{pagoId}/liberar");

        Assert.Equal("Reembolsado", reembolso.Propiedad("pago").GetProperty("estadoEscrow").GetString());
        Assert.Equal(HttpStatusCode.Conflict, liberarDespues.Estado);
    }

    [Fact]
    public async Task Una_carga_admite_un_solo_pago_en_escrow()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var cargaId = Guid.NewGuid();
        var admin = fabrica.Como(par.AdminGenerador);

        var primero = await admin.PostearAsync("/api/pagos", NuevoPago(par, cargaId));
        var segundo = await admin.PostearAsync("/api/pagos", NuevoPago(par, cargaId));

        Assert.Equal(HttpStatusCode.Created, primero.Estado);
        Assert.Equal(HttpStatusCode.Conflict, segundo.Estado);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    [InlineData(1000.123)]
    public async Task Rechaza_montos_no_validos(double monto)
    {
        var par = ParDeOrganizaciones.Nuevo();

        var respuesta = await fabrica.Como(par.AdminGenerador).PostearAsync("/api/pagos", NuevoPago(par, monto: (decimal)monto));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
    }

    [Fact]
    public async Task Dos_liberaciones_simultaneas_nunca_mueven_el_dinero_dos_veces()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var pagoId = await CrearPagoAsync(par);
        var admin = fabrica.Como(par.AdminGenerador);

        var resultados = await Task.WhenAll(
            admin.PostearAsync($"/api/pagos/{pagoId}/liberar"),
            admin.PostearAsync($"/api/pagos/{pagoId}/liberar"));

        Assert.Equal(1, resultados.Count(r => r.Estado == HttpStatusCode.OK));
        Assert.Equal(1, resultados.Count(r => r.Estado == HttpStatusCode.Conflict));

        var auditoria = await _lector.ConsultarAsync($"/api/pagos/{pagoId}/auditoria");
        Assert.Equal(2, auditoria.Cantidad);
    }

    [Fact]
    public async Task Emite_una_factura_con_numero_y_solo_una_por_carga()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var cargaId = Guid.NewGuid();
        object Factura() => new
        {
            generadorTenantId = par.GeneradorId,
            transportistaTenantId = par.TransportistaId,
            cargaId,
            monto = 850_000m
        };
        var admin = fabrica.Como(par.AdminTransportista);

        var primera = await admin.PostearAsync("/api/facturas", Factura());
        var segunda = await admin.PostearAsync("/api/facturas", Factura());

        Assert.Equal(HttpStatusCode.Created, primera.Estado);
        Assert.StartsWith("FAC-", primera.Texto("numero"));
        Assert.Equal(HttpStatusCode.Conflict, segunda.Estado);
    }

    [Fact]
    public async Task El_endpoint_de_contexto_declara_las_referencias_del_pago()
    {
        var contexto = await _lector.ConsultarAsync("/api/_meta/contexto");
        var pago = contexto.Propiedad("entidades").EnumerateArray()
            .Single(e => e.GetProperty("nombre").GetString() == "Pago");

        var destinos = pago.GetProperty("referenciasExternas").EnumerateArray()
            .ToDictionary(r => r.GetProperty("campo").GetString()!, r => r.GetProperty("contexto").GetString());

        Assert.Equal("Identity", destinos["GeneradorTenantId"]);
        Assert.Equal("Identity", destinos["TransportistaTenantId"]);
        Assert.Equal("CargoTracking", destinos["CargaId"]);
    }
}
