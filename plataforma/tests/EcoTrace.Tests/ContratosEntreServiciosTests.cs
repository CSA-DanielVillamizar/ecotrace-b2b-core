using System.Net;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

/// <summary>
/// Los contratos que cada servicio ofrece a los demás en el Trabajo 2: la autorización de pago y el
/// estado de la organización en Identity, la reserva y la liberación de recursos en Fleet, y la
/// aceptación del evento en Billing. Cada uno se prueba solo, sin el Saga.
/// </summary>
public sealed class ContratosEntreServiciosTests : IAsyncLifetime
{
    private Ecosistema _e = null!;

    public async Task InitializeAsync() => _e = await Ecosistema.IniciarAsync();

    public async Task DisposeAsync() => await _e.DisposeAsync();

    // ---- Identity: estado de la organización (ValidateTenantStatus) -------------------------

    [Fact]
    public async Task Identity_informa_el_estado_actual_de_la_organizacion_y_sube_la_version_solo_cuando_cambia()
    {
        var tenant = await _e.ClienteIdentity.PostearAsync("/api/tenants", new { nombre = "Transportes del Sur", tenantType = "Transportista" });
        var id = tenant.Id("tenantId");

        var inicial = await _e.ClienteIdentity.ConsultarAsync($"/api/tenants/{id}/estado");
        Assert.Equal("Activo", inicial.Texto("estado"));
        Assert.Equal(1, inicial.Propiedad("version").GetInt32());

        await _e.ClienteIdentity.PostearAsync($"/api/tenants/{id}/suspension");
        await _e.ClienteIdentity.PostearAsync($"/api/tenants/{id}/suspension");
        var suspendida = await _e.ClienteIdentity.ConsultarAsync($"/api/tenants/{id}/estado");
        Assert.Equal("Suspendido", suspendida.Texto("estado"));
        Assert.Equal(2, suspendida.Propiedad("version").GetInt32());

        await _e.ClienteIdentity.PostearAsync($"/api/tenants/{id}/reactivacion");
        var reactivada = await _e.ClienteIdentity.ConsultarAsync($"/api/tenants/{id}/estado");
        Assert.Equal("Activo", reactivada.Texto("estado"));
        Assert.Equal(3, reactivada.Propiedad("version").GetInt32());

        Assert.Equal(HttpStatusCode.NotFound, (await _e.ClienteIdentity.ConsultarAsync($"/api/tenants/{Guid.NewGuid()}/estado")).Estado);
    }

    // ---- Identity: autorización de pago -------------------------------------------------------

    [Fact]
    public async Task Autorizar_dos_veces_el_mismo_pago_es_idempotente_y_devuelve_la_misma_autorizacion()
    {
        var transportista = await CrearTransportistaAsync();
        var pagoId = Guid.NewGuid();

        var primera = await _e.ClienteIdentity.PostearAsync("/api/autorizaciones-pago", new { pagoId, tenantId = transportista });
        var repetida = await _e.ClienteIdentity.PostearAsync("/api/autorizaciones-pago", new { pagoId, tenantId = transportista });

        Assert.Equal(HttpStatusCode.Created, primera.Estado);
        Assert.Equal(HttpStatusCode.OK, repetida.Estado);
        Assert.Equal(primera.Id("autorizacionId"), repetida.Id("autorizacionId"));
    }

    [Fact]
    public async Task Autorizar_el_mismo_pago_a_la_vez_deja_una_sola_autorizacion()
    {
        var transportista = await CrearTransportistaAsync();
        var pagoId = Guid.NewGuid();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            _e.ClienteIdentity.PostearAsync("/api/autorizaciones-pago", new { pagoId, tenantId = transportista })));

        Assert.Equal(1, respuestas.Count(r => r.Estado == HttpStatusCode.Created));
        Assert.All(respuestas, r => Assert.True(r.Estado is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.Single(respuestas.Select(r => r.Id("autorizacionId")).Distinct());
    }

    [Fact]
    public async Task Una_autorizacion_revocada_no_se_reutiliza_y_revocar_es_idempotente()
    {
        var transportista = await CrearTransportistaAsync();
        var pagoId = Guid.NewGuid();
        await _e.ClienteIdentity.PostearAsync("/api/autorizaciones-pago", new { pagoId, tenantId = transportista });

        var revocada = await _e.ClienteIdentity.PostearAsync($"/api/autorizaciones-pago/{pagoId}/revocacion");
        var otraVez = await _e.ClienteIdentity.PostearAsync($"/api/autorizaciones-pago/{pagoId}/revocacion");
        var reintento = await _e.ClienteIdentity.PostearAsync("/api/autorizaciones-pago", new { pagoId, tenantId = transportista });

        Assert.Equal("Revocado", revocada.Texto("estado"));
        Assert.Equal(HttpStatusCode.OK, otraVez.Estado);
        Assert.Equal(HttpStatusCode.Conflict, reintento.Estado);
        Assert.Contains("revocada", reintento.Texto("detail"));
    }

    [Fact]
    public async Task No_se_autoriza_a_una_organizacion_suspendida_a_un_generador_ni_a_una_que_no_existe()
    {
        var transportista = await CrearTransportistaAsync();
        await _e.ClienteIdentity.PostearAsync($"/api/tenants/{transportista}/suspension");
        var generador = (await _e.ClienteIdentity.PostearAsync("/api/tenants", new { nombre = "Generadora Norte", tenantType = "Generador" })).Id("tenantId");

        var suspendida = await _e.ClienteIdentity.PostearAsync("/api/autorizaciones-pago", new { pagoId = Guid.NewGuid(), tenantId = transportista });
        var deGenerador = await _e.ClienteIdentity.PostearAsync("/api/autorizaciones-pago", new { pagoId = Guid.NewGuid(), tenantId = generador });
        var inexistente = await _e.ClienteIdentity.PostearAsync("/api/autorizaciones-pago", new { pagoId = Guid.NewGuid(), tenantId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Conflict, suspendida.Estado);
        Assert.Equal(HttpStatusCode.BadRequest, deGenerador.Estado);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.Estado);
    }

    // ---- Fleet: reservar y liberar recursos --------------------------------------------------

    [Fact]
    public async Task Reservar_y_liberar_son_idempotentes_y_responden_si_cambiaron_algo()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync(conPago: false);
        var cuerpo = new { cargaId = envio.CargaId, vehiculoId = envio.VehiculoId, conductorId = envio.ConductorId };

        // PrepararEnvioEnTransito ya reservó: repetir no cambia nada.
        var reserva = await _e.ClienteFleet.PostearAsync("/api/reservas", cuerpo);
        Assert.Equal(HttpStatusCode.OK, reserva.Estado);
        Assert.False(reserva.Propiedad("cambioEstado").GetBoolean());

        var liberacion = await _e.ClienteFleet.PostearAsync("/api/liberaciones", cuerpo);
        Assert.Equal(HttpStatusCode.OK, liberacion.Estado);
        Assert.True(liberacion.Propiedad("cambioEstado").GetBoolean());
        Assert.Equal("Disponible", liberacion.Texto("estadoVehiculo"));

        var otraVez = await _e.ClienteFleet.PostearAsync("/api/liberaciones", cuerpo);
        Assert.Equal(HttpStatusCode.OK, otraVez.Estado);
        Assert.False(otraVez.Propiedad("cambioEstado").GetBoolean());
    }

    [Fact]
    public async Task Un_recurso_reservado_para_una_carga_no_se_reserva_ni_se_libera_desde_otra()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync(conPago: false);
        var otraCarga = Guid.NewGuid();

        var reservar = await _e.ClienteFleet.PostearAsync("/api/reservas",
            new { cargaId = otraCarga, vehiculoId = envio.VehiculoId, conductorId = envio.ConductorId });
        var liberar = await _e.ClienteFleet.PostearAsync("/api/liberaciones",
            new { cargaId = otraCarga, vehiculoId = envio.VehiculoId, conductorId = envio.ConductorId });

        Assert.Equal(HttpStatusCode.Conflict, reservar.Estado);
        Assert.Equal(HttpStatusCode.Conflict, liberar.Estado);
        Assert.Equal("Reservado", (await _e.ClienteFleet.ConsultarAsync($"/api/vehiculos/{envio.VehiculoId}")).Texto("estado"));
    }

    [Fact]
    public async Task Reservar_la_misma_carga_a_la_vez_responde_exito_a_todas_y_reserva_una_sola_vez()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync(conPago: false);
        await _e.ClienteFleet.PostearAsync("/api/liberaciones",
            new { cargaId = envio.CargaId, vehiculoId = envio.VehiculoId, conductorId = envio.ConductorId });

        // Un reintento del orquestador puede coincidir con la primera solicitud. El contrato pide 200 para
        // la repetida, no un 409 que parezca un fallo.
        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            _e.ClienteFleet.PostearAsync("/api/reservas",
                new { cargaId = envio.CargaId, vehiculoId = envio.VehiculoId, conductorId = envio.ConductorId })));

        Assert.Equal(1, respuestas.Count(r => r.Estado == HttpStatusCode.Created));
        Assert.All(respuestas, r => Assert.True(r.Estado is HttpStatusCode.Created or HttpStatusCode.OK, $"Respondió {r.Estado}"));
    }

    [Fact]
    public async Task Dos_reservas_simultaneas_del_mismo_vehiculo_para_cargas_distintas_no_pueden_ganar_las_dos()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync(conPago: false);
        var vehiculoLibre = (await _e.ClienteFleet.PostearAsync("/api/liberaciones",
            new { cargaId = envio.CargaId, vehiculoId = envio.VehiculoId, conductorId = envio.ConductorId }));
        Assert.Equal(HttpStatusCode.OK, vehiculoLibre.Estado);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            _e.ClienteFleet.PostearAsync("/api/reservas",
                new { cargaId = Guid.NewGuid(), vehiculoId = envio.VehiculoId, conductorId = envio.ConductorId })));

        Assert.Equal(1, respuestas.Count(r => r.Estado == HttpStatusCode.Created));
        Assert.Equal(3, respuestas.Count(r => r.Estado == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task El_vehiculo_y_el_conductor_de_una_reserva_deben_ser_del_mismo_transportista()
    {
        var uno = await _e.PrepararEnvioEnTransitoAsync(conPago: false);
        var otro = await _e.PrepararEnvioEnTransitoAsync(conPago: false);
        await _e.ClienteFleet.PostearAsync("/api/liberaciones", new { cargaId = uno.CargaId, vehiculoId = uno.VehiculoId, conductorId = uno.ConductorId });

        var respuesta = await _e.ClienteFleet.PostearAsync("/api/reservas",
            new { cargaId = Guid.NewGuid(), vehiculoId = uno.VehiculoId, conductorId = otro.ConductorId });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
    }

    [Fact]
    public async Task La_simulacion_de_fallos_esta_apagada_por_defecto()
    {
        await using var fleet = new ApiFactory<FleetManagementApiMarker>();
        using var cliente = fleet.CreateClient();

        var respuesta = await cliente.PostearAsync("/api/_simulacion/fallos", new { operacion = "liberaciones", cantidad = 3 });

        Assert.Equal(HttpStatusCode.NotFound, respuesta.Estado);
    }

    [Fact]
    public async Task Con_la_simulacion_habilitada_se_puede_armar_y_restablecer_desde_la_API()
    {
        var armada = await _e.ClienteFleet.PostearAsync("/api/_simulacion/fallos", new { operacion = "liberaciones", cantidad = 2 });
        Assert.Equal(2, armada.Propiedad("liberaciones").GetInt32());

        var restablecida = await _e.ClienteFleet.PostearAsync("/api/_simulacion/fallos", new { operacion = "liberaciones", cantidad = 0 });
        Assert.Equal(0, restablecida.Propiedad("liberaciones").GetInt32());

        var invalida = await _e.ClienteFleet.PostearAsync("/api/_simulacion/fallos", new { operacion = "otra", cantidad = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, invalida.Estado);
    }

    // ---- Billing: aceptar el evento ----------------------------------------------------------

    [Fact]
    public async Task Billing_rechaza_un_evento_sin_identificador_para_una_carga_sin_pago_o_con_otras_organizaciones()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        object Evento(Guid? eventId = null, Guid? cargaId = null, Guid? transportistaId = null) => new
        {
            eventId = eventId ?? Guid.NewGuid(),
            cargaId = cargaId ?? envio.CargaId,
            vehiculoId = envio.VehiculoId,
            conductorId = envio.ConductorId,
            transportistaTenantId = transportistaId ?? envio.TransportistaId
        };

        var sinId = await _e.ClienteBilling.PostearAsync("/api/eventos/entrega-confirmada",
            new { cargaId = envio.CargaId, vehiculoId = envio.VehiculoId, conductorId = envio.ConductorId });
        var sinPago = await _e.ClienteBilling.PostearAsync("/api/eventos/entrega-confirmada", Evento(cargaId: Guid.NewGuid()));
        var otroTransportista = await _e.ClienteBilling.PostearAsync("/api/eventos/entrega-confirmada", Evento(transportistaId: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, sinId.Estado);
        Assert.Equal(HttpStatusCode.NotFound, sinPago.Estado);
        Assert.Equal(HttpStatusCode.Conflict, otroTransportista.Estado);
        Assert.Equal(0, (await _e.ClienteBilling.ConsultarAsync("/api/sagas")).Cantidad);
    }

    [Fact]
    public async Task Billing_no_inicia_el_Saga_de_un_pago_que_ya_no_esta_en_custodia()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        await _e.ClienteBilling.PostearAsync($"/api/pagos/{envio.PagoId}/reembolso");

        var respuesta = await _e.ClienteBilling.PostearAsync("/api/eventos/entrega-confirmada", new
        {
            eventId = Guid.NewGuid(), cargaId = envio.CargaId, vehiculoId = envio.VehiculoId, conductorId = envio.ConductorId
        });

        Assert.Equal(HttpStatusCode.Conflict, respuesta.Estado);
        Assert.Contains("Reembolsado", respuesta.Texto("detail"));
    }

    [Fact]
    public async Task Cada_respuesta_devuelve_el_CorrelationId_y_respeta_el_que_envia_el_llamador()
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/api/pagos");
        solicitud.Headers.Add("X-Correlation-Id", "traza-abc-123");
        using var conId = await _e.ClienteBilling.SendAsync(solicitud);
        using var sinId = await _e.ClienteBilling.GetAsync("/api/pagos");

        Assert.Equal("traza-abc-123", conId.Headers.GetValues("X-Correlation-Id").Single());
        Assert.NotEmpty(sinId.Headers.GetValues("X-Correlation-Id").Single());
    }

    private async Task<Guid> CrearTransportistaAsync() =>
        (await _e.ClienteIdentity.PostearAsync("/api/tenants", new { nombre = $"Transportes {Guid.NewGuid():N}"[..20], tenantType = "Transportista" })).Id("tenantId");
}
