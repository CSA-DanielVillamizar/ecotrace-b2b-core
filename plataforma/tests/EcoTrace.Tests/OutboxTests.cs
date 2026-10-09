using System.Net;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

/// <summary>
/// El Transactional Outbox de Cargo &amp; Tracking (ADR 0002, sección 3): el evento nace con la entrega,
/// sobrevive a una caída de Billing, se reintenta con espera creciente y, si no hay remedio, queda en la
/// cola de mensajes muertos hasta que una persona lo reprocese.
/// </summary>
public sealed class OutboxTests : IAsyncLifetime
{
    private Ecosistema _e = null!;

    public async Task InitializeAsync() => _e = await Ecosistema.IniciarAsync();

    public async Task DisposeAsync() => await _e.DisposeAsync();

    [Fact]
    public async Task Solo_la_entrega_genera_evento_y_lo_guarda_una_sola_vez()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();

        // Estar en tránsito, asignada o con novedad no es una entrega: no hay nada que publicar.
        Assert.Equal(0, (await _e.ClienteCargo.ConsultarAsync("/api/outbox")).Cantidad);

        await _e.EntregarAsync(envio);
        var mensajes = await _e.ClienteCargo.ConsultarAsync("/api/outbox");

        Assert.Equal(1, mensajes.Cantidad);
        Assert.Equal("EntregaConfirmada", mensajes.Json[0].GetProperty("tipo").GetString());
        Assert.Equal("Pendiente", mensajes.Json[0].GetProperty("estado").GetString());

        // Volver a entregar la misma carga es un conflicto y no agrega un segundo mensaje.
        var repetida = await _e.ClienteCargo.PostearAsync(
            $"/api/cargas/{envio.CargaId}/seguimientos", new { estado = "Entregado", ubicacion = "Popayán" });
        Assert.Equal(HttpStatusCode.Conflict, repetida.Estado);
        Assert.Equal(1, (await _e.ClienteCargo.ConsultarAsync("/api/outbox")).Cantidad);
    }

    [Fact]
    public async Task Entregar_la_misma_carga_a_la_vez_deja_una_sola_entrega_y_un_solo_mensaje()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();

        // Todas parten de EnTransito. Sin un token de concurrencia en el estado de la carga, dos podrían
        // pasar la validación y guardar su propio mensaje con un identificador distinto.
        var respuestas = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            _e.ClienteCargo.PostearAsync(
                $"/api/cargas/{envio.CargaId}/seguimientos", new { estado = "Entregado", ubicacion = "Popayán" })));

        Assert.Equal(1, respuestas.Count(r => r.Estado == HttpStatusCode.Created));
        Assert.All(respuestas, r => Assert.True(r.Estado is HttpStatusCode.Created or HttpStatusCode.Conflict, $"Respondió {r.Estado}"));
        Assert.Equal(1, (await _e.ClienteCargo.ConsultarAsync("/api/outbox")).Cantidad);
    }

    [Fact]
    public async Task Si_Billing_esta_caido_la_entrega_se_registra_igual_y_el_evento_llega_cuando_Billing_vuelve()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        _e.BillingDesdeCargo.Caido = true;

        // La entrega no depende de Billing: se registra aunque no haya conexión.
        var entrega = await _e.EntregarAsync(envio);
        Assert.Equal("Entregado", entrega.Propiedad("carga").GetProperty("estado").GetString());

        Assert.Equal(0, await _e.PublicarOutboxAsync());
        var mensaje = (await _e.ClienteCargo.ConsultarAsync("/api/outbox")).Json[0];
        Assert.Equal("Pendiente", mensaje.GetProperty("estado").GetString());
        Assert.Equal(1, mensaje.GetProperty("intentos").GetInt32());
        Assert.Contains("Sin conexión con Billing", mensaje.GetProperty("ultimoError").GetString());

        // Billing vuelve: el mensaje que esperaba se entrega y el Saga arranca. No se perdió nada.
        _e.BillingDesdeCargo.Caido = false;
        Assert.Equal(1, await _e.PublicarOutboxAsync());
        await _e.AvanzarSagasAsync();

        Assert.Equal("Liberado", (await _e.PagoAsync(envio.PagoId!.Value)).Propiedad("pago").GetProperty("estadoEscrow").GetString());
        Assert.Equal("Publicado", (await _e.ClienteCargo.ConsultarAsync("/api/outbox")).Json[0].GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Tras_agotar_los_intentos_el_mensaje_pasa_a_muertos_y_se_puede_reprocesar()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        _e.BillingDesdeCargo.Caido = true;
        await _e.EntregarAsync(envio);

        // Tres intentos fallidos (el máximo configurado para la prueba) y el mensaje queda muerto.
        for (var intento = 0; intento < 3; intento++)
        {
            Assert.Equal(0, await _e.PublicarOutboxAsync());
        }

        var muerto = (await _e.ClienteCargo.ConsultarAsync("/api/outbox?estado=Muerto")).Json[0];
        Assert.Equal(3, muerto.GetProperty("intentos").GetInt32());

        // Una vez muerto, el publicador ya no lo toca aunque Billing vuelva.
        _e.BillingDesdeCargo.Caido = false;
        Assert.Equal(0, await _e.PublicarOutboxAsync());

        // Una persona lo reprocesa y esta vez llega.
        var eventoId = muerto.GetProperty("eventoId").GetGuid();
        var reprocesado = await _e.ClienteCargo.PostearAsync($"/api/outbox/{eventoId}/reprocesar");
        Assert.Equal("Pendiente", reprocesado.Texto("estado"));
        Assert.Equal(0, reprocesado.Propiedad("intentos").GetInt32());

        Assert.Equal(1, await _e.PublicarOutboxAsync());
        await _e.AvanzarSagasAsync();
        Assert.Equal("Liberado", (await _e.PagoAsync(envio.PagoId!.Value)).Propiedad("pago").GetProperty("estadoEscrow").GetString());
    }

    [Fact]
    public async Task Un_rechazo_permanente_deja_el_mensaje_muerto_de_inmediato_sin_gastar_reintentos()
    {
        // Carga entregada, pero Billing no tiene ningún pago para ella: no es un fallo pasajero.
        var envio = await _e.PrepararEnvioEnTransitoAsync(conPago: false);
        await _e.EntregarAsync(envio);

        Assert.Equal(0, await _e.PublicarOutboxAsync());

        var muerto = (await _e.ClienteCargo.ConsultarAsync("/api/outbox?estado=Muerto")).Json[0];
        Assert.Equal(1, muerto.GetProperty("intentos").GetInt32());
        Assert.Contains("404", muerto.GetProperty("ultimoError").GetString());
        Assert.Equal(1, _e.BillingDesdeCargo.Llamadas);

        // Se crea el pago que faltaba, se reprocesa y el flujo sigue desde donde quedó.
        await _e.ClienteBilling.PostearAsync("/api/pagos", new
        {
            generadorTenantId = envio.GeneradorId, transportistaTenantId = envio.TransportistaId,
            cargaId = envio.CargaId, monto = 500_000m
        });
        await _e.ClienteCargo.PostearAsync($"/api/outbox/{muerto.GetProperty("eventoId").GetGuid()}/reprocesar");
        Assert.Equal(1, await _e.PublicarOutboxAsync());
        await _e.AvanzarSagasAsync();

        Assert.Equal(1, (await _e.ClienteBilling.ConsultarAsync("/api/sagas?estado=Completada")).Cantidad);
    }

    [Fact]
    public async Task Solo_un_mensaje_muerto_se_puede_reprocesar()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        await _e.EntregarAsync(envio);
        var eventoId = (await _e.ClienteCargo.ConsultarAsync("/api/outbox")).Json[0].GetProperty("eventoId").GetGuid();

        var respuesta = await _e.ClienteCargo.PostearAsync($"/api/outbox/{eventoId}/reprocesar");

        Assert.Equal(HttpStatusCode.Conflict, respuesta.Estado);
    }
}
