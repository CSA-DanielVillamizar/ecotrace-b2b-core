using System.Net;
using System.Text.Json;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

/// <summary>
/// El Saga "Liberar Pago en Escrow" (ADR 0003) de punta a punta: cuatro servicios reales, cada uno con
/// su base de datos, hablándose por HTTP. Cada prueba recorre un desenlace distinto: el camino feliz, la
/// recuperación con reintentos, la compensación, el fallo cerrado y la idempotencia.
/// </summary>
public sealed class SagaLiberacionPagoTests : IAsyncLifetime
{
    private Ecosistema _e = null!;

    public async Task InitializeAsync() => _e = await Ecosistema.IniciarAsync();

    public async Task DisposeAsync() => await _e.DisposeAsync();

    [Fact]
    public async Task Camino_feliz_entregar_la_carga_libera_el_pago_y_los_recursos_a_traves_de_los_cuatro_contextos()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        var pagoId = envio.PagoId!.Value;
        await _e.EntregarAsync(envio);

        // Cargo & Tracking dejó el evento guardado junto con la entrega y todavía no llamó a nadie.
        var pendientes = await _e.ClienteCargo.ConsultarAsync("/api/outbox?estado=Pendiente");
        Assert.Equal(1, pendientes.Cantidad);
        Assert.Equal(0, _e.BillingDesdeCargo.Llamadas);

        // Billing acepta el evento y crea el Saga, pero el dinero no se mueve hasta que el Saga corre.
        Assert.Equal(1, await _e.PublicarOutboxAsync());
        var aceptado = await _e.SagaDelPagoAsync(pagoId);
        Assert.Equal("EnCurso", aceptado.Texto("estado"));
        Assert.Equal("EnCustodia", await EstadoDelPago(pagoId));

        await _e.AvanzarSagasAsync();

        var saga = await _e.SagaDelPagoAsync(pagoId);
        Assert.Equal("Completada", saga.Texto("estado"));
        Assert.All(saga.Propiedad("pasos").EnumerateArray(), paso => Assert.Equal("Completado", paso.GetProperty("estado").GetString()));

        // Cada contexto terminó en el estado que le corresponde, comprobado por su propia API.
        Assert.Equal("Liberado", await EstadoDelPago(pagoId));
        var autorizacion = await _e.ClienteIdentity.ConsultarAsync($"/api/autorizaciones-pago/{pagoId}");
        Assert.Equal("Autorizado", autorizacion.Texto("estado"));
        Assert.Equal("Disponible", (await _e.ClienteFleet.ConsultarAsync($"/api/vehiculos/{envio.VehiculoId}")).Texto("estado"));
        Assert.Equal("Disponible", (await _e.ClienteFleet.ConsultarAsync($"/api/conductores/{envio.ConductorId}")).Texto("estado"));

        // El mismo CorrelationId viajó desde la entrega en Cargo hasta el Saga en Billing.
        Assert.Equal(pendientes.Json[0].GetProperty("correlationId").GetString(), saga.Texto("correlationId"));

        var acciones = await AccionesDeAuditoria(pagoId);
        Assert.Contains("Autorización de Identity obtenida", acciones);
        Assert.Contains("Fondos liberados al transportista", acciones);
        Assert.Contains("Vehículo y conductor liberados en Fleet", acciones);
        Assert.Contains("Saga de liberación completada", acciones);
    }

    [Fact]
    public async Task Si_Fleet_falla_dos_veces_el_Saga_se_recupera_con_reintentos_y_completa()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        _e.FallosDeFleet.Armar("liberaciones", 2);

        await _e.EntregarYProcesarAsync(envio);

        var saga = await _e.SagaDelPagoAsync(envio.PagoId!.Value);
        Assert.Equal("Completada", saga.Texto("estado"));

        var paso = Paso(saga, "LiberarRecursos");
        Assert.Equal("Completado", paso.GetProperty("estado").GetString());
        Assert.Equal(2, paso.GetProperty("intentos").GetInt32());
        Assert.Equal("Liberado", await EstadoDelPago(envio.PagoId.Value));
        Assert.Equal("Disponible", (await _e.ClienteFleet.ConsultarAsync($"/api/vehiculos/{envio.VehiculoId}")).Texto("estado"));
    }

    [Fact]
    public async Task Si_Fleet_no_se_recupera_el_Saga_compensa_y_revierte_el_estado_de_los_pasos_anteriores()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        var pagoId = envio.PagoId!.Value;
        _e.FallosDeFleet.Armar("liberaciones", 100);

        await _e.EntregarYProcesarAsync(envio);

        var saga = await _e.SagaDelPagoAsync(pagoId);
        Assert.Equal("Compensada", saga.Texto("estado"));
        Assert.Contains("No se pudieron liberar los recursos", saga.Texto("motivo"));

        // El paso que falló agotó sus cuatro intentos (el primero y tres reintentos, como pide el ADR 0002).
        var recursos = Paso(saga, "LiberarRecursos");
        Assert.Equal("Fallido", recursos.GetProperty("estado").GetString());
        Assert.Equal(4, recursos.GetProperty("intentos").GetInt32());

        // Los dos pasos que sí se habían completado quedaron compensados, y el último nunca se ejecutó.
        Assert.Equal("Compensado", Paso(saga, "LiberarFondos").GetProperty("estado").GetString());
        Assert.Equal("Compensado", Paso(saga, "AutorizarPago").GetProperty("estado").GetString());
        Assert.Equal("Pendiente", Paso(saga, "RegistrarAuditoria").GetProperty("estado").GetString());

        // La compensación revirtió el estado real en cada contexto.
        Assert.Equal("EnDisputa", await EstadoDelPago(pagoId));
        Assert.Equal("Revocado", (await _e.ClienteIdentity.ConsultarAsync($"/api/autorizaciones-pago/{pagoId}")).Texto("estado"));

        // Fleet nunca recibió la liberación: el vehículo sigue reservado para la carga. Nada quedó a medias sin explicación.
        var vehiculo = await _e.ClienteFleet.ConsultarAsync($"/api/vehiculos/{envio.VehiculoId}");
        Assert.Equal("Reservado", vehiculo.Texto("estado"));

        // Y la auditoría cuenta lo ocurrido, en orden: se liberó, se revirtió y se revocó.
        var acciones = await AccionesDeAuditoria(pagoId);
        Assert.Contains("Fondos liberados al transportista", acciones);
        Assert.Contains("Liberación revertida: fondos en disputa (nota de débito interna)", acciones);
        Assert.Contains("Autorización de Identity revocada", acciones);
        Assert.DoesNotContain("Saga de liberación completada", acciones);
    }

    [Fact]
    public async Task Una_organizacion_suspendida_hace_fallar_el_Saga_en_el_primer_paso_sin_mover_dinero()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        var pagoId = envio.PagoId!.Value;
        Assert.Equal(HttpStatusCode.OK, (await _e.ClienteIdentity.PostearAsync($"/api/tenants/{envio.TransportistaId}/suspension")).Estado);

        await _e.EntregarYProcesarAsync(envio);

        var saga = await _e.SagaDelPagoAsync(pagoId);
        Assert.Equal("Fallida", saga.Texto("estado"));
        Assert.Contains("suspendida", saga.Texto("motivo"));
        Assert.Equal("Fallido", Paso(saga, "AutorizarPago").GetProperty("estado").GetString());

        // No se hizo nada, así que no hubo nada que compensar: el dinero sigue en custodia.
        Assert.Equal("EnCustodia", await EstadoDelPago(pagoId));
        Assert.Equal("Pendiente", Paso(saga, "LiberarFondos").GetProperty("estado").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _e.ClienteIdentity.ConsultarAsync($"/api/autorizaciones-pago/{pagoId}")).Estado);
        Assert.Equal("Reservado", (await _e.ClienteFleet.ConsultarAsync($"/api/vehiculos/{envio.VehiculoId}")).Texto("estado"));
    }

    [Fact]
    public async Task Si_Identity_no_responde_el_Saga_falla_cerrado_despues_de_los_reintentos_del_ADR_0002()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        _e.IdentityDesdeBilling.Caido = true;

        await _e.EntregarYProcesarAsync(envio);

        var saga = await _e.SagaDelPagoAsync(envio.PagoId!.Value);
        Assert.Equal("Fallida", saga.Texto("estado"));
        Assert.Contains("No hay conexión con Identity", saga.Texto("motivo"));

        // Una llamada inicial y dos reintentos: la política del ADR 0002 para la validación síncrona.
        Assert.Equal(3, _e.IdentityDesdeBilling.Llamadas);

        // Fallar cerrado significa que sin la respuesta de Identity el dinero no se mueve.
        Assert.Equal("EnCustodia", await EstadoDelPago(envio.PagoId.Value));
    }

    [Fact]
    public async Task Si_Identity_deja_de_responder_al_compensar_el_Saga_pide_intervencion_en_vez_de_fallar_en_silencio()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        var pagoId = envio.PagoId!.Value;

        // Identity atiende las dos primeras llamadas (autorizar y verificar) y cae justo después: antes de
        // que haga falta revocar. Fleet además falla siempre, así que el Saga tiene que compensar.
        _e.IdentityDesdeBilling.CaidoDesdeLlamada = 3;
        _e.FallosDeFleet.Armar("liberaciones", 100);

        await _e.EntregarYProcesarAsync(envio);

        var saga = await _e.SagaDelPagoAsync(pagoId);
        Assert.Equal("RequiereIntervencion", saga.Texto("estado"));
        Assert.Contains("AutorizarPago", saga.Texto("motivo"));

        // Lo que sí se pudo compensar quedó compensado, y lo que no, a la vista.
        Assert.Equal("EnDisputa", await EstadoDelPago(pagoId));
        Assert.Equal("Compensado", Paso(saga, "LiberarFondos").GetProperty("estado").GetString());
        Assert.Equal("Completado", Paso(saga, "AutorizarPago").GetProperty("estado").GetString());
        Assert.Equal(4, Paso(saga, "AutorizarPago").GetProperty("intentosCompensacion").GetInt32());
    }

    [Fact]
    public async Task El_mismo_evento_entregado_dos_veces_crea_un_solo_Saga_y_no_mueve_el_dinero_dos_veces()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        var evento = Evento(Guid.NewGuid(), envio);

        var primera = await _e.ClienteBilling.PostearAsync("/api/eventos/entrega-confirmada", evento);
        var repetida = await _e.ClienteBilling.PostearAsync("/api/eventos/entrega-confirmada", evento);

        Assert.Equal(HttpStatusCode.Accepted, primera.Estado);
        Assert.Equal(HttpStatusCode.OK, repetida.Estado);
        Assert.Equal("duplicado", repetida.Texto("resultado"));

        await _e.AvanzarSagasAsync();

        Assert.Equal(1, (await _e.ClienteBilling.ConsultarAsync($"/api/sagas?pagoId={envio.PagoId}")).Cantidad);
        Assert.Equal(1, (await AccionesDeAuditoria(envio.PagoId!.Value)).Count(a => a == "Fondos liberados al transportista"));
    }

    [Fact]
    public async Task Un_segundo_evento_para_el_mismo_pago_no_crea_otro_Saga_porque_la_clave_de_negocio_es_el_pago()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();

        var primero = await _e.ClienteBilling.PostearAsync("/api/eventos/entrega-confirmada", Evento(Guid.NewGuid(), envio));
        var otro = await _e.ClienteBilling.PostearAsync("/api/eventos/entrega-confirmada", Evento(Guid.NewGuid(), envio));

        Assert.Equal(HttpStatusCode.Accepted, primero.Estado);
        Assert.Equal(HttpStatusCode.OK, otro.Estado);
        Assert.Equal(1, (await _e.ClienteBilling.ConsultarAsync("/api/sagas")).Cantidad);
    }

    [Fact]
    public async Task Dos_ejecutores_avanzando_el_mismo_Saga_a_la_vez_no_duplican_ningun_efecto()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        await _e.EntregarAsync(envio);
        await _e.PublicarOutboxAsync();
        var sagaId = (await _e.SagaDelPagoAsync(envio.PagoId!.Value)).Id("sagaId");

        // Identity tarda un poco en responder para que los dos ejecutores se solapen de verdad: los dos leen
        // el Saga en el mismo estado y compiten por guardar el mismo paso. Sin ese retraso, uno terminaría
        // antes de que el otro empezara y la carrera no se produciría.
        _e.IdentityDesdeBilling.Retraso = TimeSpan.FromMilliseconds(300);
        await Task.WhenAll(_e.EjecutorDeBilling.AvanzarAsync(sagaId), _e.EjecutorDeBilling.AvanzarAsync(sagaId));
        _e.IdentityDesdeBilling.Retraso = TimeSpan.Zero;
        await _e.AvanzarSagasAsync();

        var saga = await _e.SagaDelPagoAsync(envio.PagoId.Value);
        Assert.Equal("Completada", saga.Texto("estado"));
        Assert.Equal("Liberado", await EstadoDelPago(envio.PagoId.Value));

        var acciones = await AccionesDeAuditoria(envio.PagoId.Value);
        Assert.Equal(1, acciones.Count(a => a == "Fondos liberados al transportista"));
        Assert.Equal(1, acciones.Count(a => a == "Saga de liberación completada"));

        // Ninguna acción de la auditoría aparece dos veces: el ejecutor que perdió la carrera no dejó rastro.
        Assert.Equal(acciones.Count, acciones.Distinct().Count());
    }

    [Fact]
    public async Task Si_alguien_libera_el_pago_a_mano_antes_que_el_Saga_el_Saga_compensa_y_no_libera_dos_veces()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        var pagoId = envio.PagoId!.Value;
        await _e.EntregarAsync(envio);
        await _e.PublicarOutboxAsync();

        // Un operador libera el pago por la ruta manual mientras el Saga todavía no corrió.
        Assert.Equal(HttpStatusCode.OK, (await _e.ClienteBilling.PostearAsync($"/api/pagos/{pagoId}/liberar")).Estado);

        await _e.AvanzarSagasAsync();

        var saga = await _e.SagaDelPagoAsync(pagoId);
        Assert.Equal("Compensada", saga.Texto("estado"));
        Assert.Contains("ya está Liberado", saga.Texto("motivo"));
        Assert.Equal(1, (await AccionesDeAuditoria(pagoId)).Count(a => a == "Fondos liberados al transportista"));
        Assert.Equal("Revocado", (await _e.ClienteIdentity.ConsultarAsync($"/api/autorizaciones-pago/{pagoId}")).Texto("estado"));
    }

    private static object Evento(Guid eventId, Escenario envio) => new
    {
        eventId,
        eventType = "EntregaConfirmada",
        occurredAt = DateTime.UtcNow,
        cargaId = envio.CargaId,
        vehiculoId = envio.VehiculoId,
        conductorId = envio.ConductorId,
        generadorTenantId = envio.GeneradorId,
        transportistaTenantId = envio.TransportistaId,
        correlationId = "prueba-correlacion"
    };

    private static JsonElement Paso(Respuesta saga, string nombre) =>
        saga.Propiedad("pasos").EnumerateArray().Single(p => p.GetProperty("nombre").GetString() == nombre);

    private async Task<string> EstadoDelPago(Guid pagoId) =>
        (await _e.PagoAsync(pagoId)).Propiedad("pago").GetProperty("estadoEscrow").GetString()!;

    private async Task<List<string>> AccionesDeAuditoria(Guid pagoId) =>
        (await _e.PagoAsync(pagoId)).Propiedad("auditoria").EnumerateArray()
            .Select(a => a.GetProperty("accion").GetString()!)
            .ToList();
}

/// <summary>
/// El mismo fallo cerrado, pero por un Identity lento en vez de caído. Tiene su propia clase porque
/// necesita un tiempo de espera corto configurado desde el arranque de los servicios.
/// </summary>
public sealed class SagaConIdentityLentoTests : IAsyncLifetime
{
    private Ecosistema _e = null!;

    public async Task InitializeAsync() => _e = await Ecosistema.IniciarAsync(new OpcionesEcosistema(TimeoutIdentityMs: 150));

    public async Task DisposeAsync() => await _e.DisposeAsync();

    [Fact]
    public async Task Un_Identity_que_tarda_mas_que_el_tiempo_maximo_agota_los_reintentos_y_el_Saga_falla_cerrado()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        _e.IdentityDesdeBilling.Retraso = TimeSpan.FromSeconds(2);

        await _e.EntregarYProcesarAsync(envio);

        var saga = await _e.SagaDelPagoAsync(envio.PagoId!.Value);
        Assert.Equal("Fallida", saga.Texto("estado"));
        Assert.Contains("no respondió en 150 ms", saga.Texto("motivo"));
        Assert.Equal(3, _e.IdentityDesdeBilling.Llamadas);
        Assert.Equal("EnCustodia", (await _e.PagoAsync(envio.PagoId.Value)).Propiedad("pago").GetProperty("estadoEscrow").GetString());
    }
}
