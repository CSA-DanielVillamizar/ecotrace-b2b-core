using System.Net;
using System.Net.Http.Json;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

/// <summary>
/// Idempotencia de la confirmación de entrega por OperationId (Trabajo 3, ADR 0005). La app del conductor
/// reenvía la misma acción con el mismo <c>Idempotency-Key</c> si no sabe si llegó. Estas pruebas demuestran
/// que el servidor de Cargo &amp; Tracking no repite el efecto (ni el seguimiento, ni el mensaje del Outbox, ni
/// el Saga de pago) y que, cuando rechaza, dice de qué tipo es el conflicto.
/// Cada prueba levanta su propio ecosistema, así los conteos del Outbox no se mezclan entre pruebas.
/// </summary>
public sealed class IdempotenciaCargoTests : IAsyncLifetime
{
    private const string EncabezadoIdempotencia = "Idempotency-Key";

    private Ecosistema _e = null!;

    public async Task InitializeAsync() => _e = await Ecosistema.IniciarAsync();

    public async Task DisposeAsync() => await _e.DisposeAsync();

    /// <summary>
    /// Registra un seguimiento de la carga, con la clave de idempotencia si se da una. Se arma la solicitud a
    /// mano porque el apoyo común de las pruebas no permite agregar encabezados.
    /// </summary>
    private async Task<Respuesta> EnviarAsync(Escenario envio, string? clave, string estado = "Entregado", string? nota = null)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, $"/api/cargas/{envio.CargaId}/seguimientos")
        {
            Content = JsonContent.Create(new { estado, ubicacion = "Popayán", nota })
        };

        if (clave is not null)
        {
            solicitud.Headers.Add(EncabezadoIdempotencia, clave);
        }

        return await Respuesta.De(await _e.ClienteCargo.SendAsync(solicitud));
    }

    private async Task<int> MensajesEnElOutboxAsync() =>
        (await _e.ClienteCargo.ConsultarAsync("/api/outbox")).Cantidad;

    [Fact]
    public async Task Reintentar_con_la_misma_clave_responde_200_con_el_mismo_resultado_y_no_repite_nada()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        var clave = Guid.NewGuid().ToString();

        var primera = await EnviarAsync(envio, clave);
        Assert.Equal(HttpStatusCode.Created, primera.Estado);

        // El reintento no es un error: la acción ya estaba hecha, así que recibe el resultado de la primera vez.
        var reintento = await EnviarAsync(envio, clave);
        Assert.Equal(HttpStatusCode.OK, reintento.Estado);
        Assert.Equal("Entregado", reintento.Propiedad("carga").GetProperty("estado").GetString());
        Assert.Equal(
            primera.Propiedad("seguimientos").EnumerateArray().Last().GetProperty("seguimientoId").GetGuid(),
            reintento.Propiedad("seguimientos").EnumerateArray().Last().GetProperty("seguimientoId").GetGuid());

        // No se duplicó nada: un solo mensaje en el Outbox y una línea de tiempo de tres eventos.
        Assert.Equal(1, await MensajesEnElOutboxAsync());
        var linea = await _e.ClienteCargo.ConsultarAsync($"/api/cargas/{envio.CargaId}/seguimientos");
        Assert.Equal(3, linea.Cantidad);
    }

    [Fact]
    public async Task El_reintento_no_dispara_el_Saga_de_pago_dos_veces()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        var clave = Guid.NewGuid().ToString();

        Assert.Equal(HttpStatusCode.Created, (await EnviarAsync(envio, clave)).Estado);
        Assert.Equal(HttpStatusCode.OK, (await EnviarAsync(envio, clave)).Estado);

        // Hay un solo mensaje para publicar, y después no queda ninguno.
        Assert.Equal(1, await _e.PublicarOutboxAsync());
        Assert.Equal(0, await _e.PublicarOutboxAsync());
        await _e.AvanzarSagasAsync();

        // Billing tiene un solo Saga para el pago y el dinero se liberó una sola vez.
        Assert.Equal(1, (await _e.ClienteBilling.ConsultarAsync($"/api/sagas?pagoId={envio.PagoId}")).Cantidad);
        Assert.Equal("Liberado", (await _e.PagoAsync(envio.PagoId!.Value)).Propiedad("pago").GetProperty("estadoEscrow").GetString());
    }

    [Fact]
    public async Task Reintentos_simultaneos_con_la_misma_clave_dejan_una_sola_entrega_y_un_solo_mensaje()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        var clave = Guid.NewGuid().ToString();

        // Seis envíos de la misma acción a la vez, como un teléfono que reintenta apenas vuelve la señal.
        // Solo uno puede ganar la carrera (201); los demás son reintentos y reciben el resultado (200), nunca
        // un 409 que la app tomaría por un rechazo.
        var respuestas = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => EnviarAsync(envio, clave)));

        Assert.Equal(1, respuestas.Count(r => r.Estado == HttpStatusCode.Created));
        Assert.All(respuestas, r => Assert.True(r.Estado is HttpStatusCode.Created or HttpStatusCode.OK, $"Respondió {r.Estado}"));
        Assert.Equal(1, await MensajesEnElOutboxAsync());
    }

    [Fact]
    public async Task Una_clave_distinta_sobre_una_carga_ya_entregada_es_un_conflicto_de_tipo_B()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        Assert.Equal(HttpStatusCode.Created, (await EnviarAsync(envio, Guid.NewGuid().ToString())).Estado);

        // Otra operación distinta (otra clave) intenta entregar lo que ya está entregado: el servidor manda.
        var otra = await EnviarAsync(envio, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Conflict, otra.Estado);
        Assert.Equal("B", otra.Texto("tipoConflicto"));
        Assert.Equal(1, await MensajesEnElOutboxAsync());
    }

    [Fact]
    public async Task Sin_clave_un_Entregado_repetido_sigue_siendo_conflicto_de_tipo_B()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        await _e.EntregarAsync(envio);

        var repetida = await EnviarAsync(envio, clave: null);

        Assert.Equal(HttpStatusCode.Conflict, repetida.Estado);
        Assert.Equal("B", repetida.Texto("tipoConflicto"));
        Assert.Equal(1, await MensajesEnElOutboxAsync());
    }

    [Fact]
    public async Task Una_validacion_rechazada_dice_que_es_de_tipo_A()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();

        // Una novedad sin nota no cumple una regla, pero el conductor puede corregirla.
        var sinNota = await EnviarAsync(envio, clave: null, estado: "ConNovedad", nota: null);

        Assert.Equal(HttpStatusCode.BadRequest, sinNota.Estado);
        Assert.Equal("A", sinNota.Texto("tipoConflicto"));
    }

    [Fact]
    public async Task Una_clave_mal_formada_se_rechaza_con_400_de_tipo_A_y_no_cambia_nada()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();

        var respuesta = await EnviarAsync(envio, clave: "hola");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Equal("A", respuesta.Texto("tipoConflicto"));
        Assert.Equal(0, await MensajesEnElOutboxAsync());
        var carga = await _e.ClienteCargo.ConsultarAsync($"/api/cargas/{envio.CargaId}");
        Assert.Equal("EnTransito", carga.Propiedad("carga").GetProperty("estado").GetString());
    }

    [Fact]
    public async Task La_misma_clave_no_sirve_para_otra_carga()
    {
        var primera = await _e.PrepararEnvioEnTransitoAsync();
        var segunda = await _e.PrepararEnvioEnTransitoAsync();
        var clave = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.Created, (await EnviarAsync(primera, clave)).Estado);

        // Devolverle a la segunda carga el resultado de la primera mostraría datos que no le corresponden.
        var respuesta = await EnviarAsync(segunda, clave);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Contains("otra carga", respuesta.Texto("detail"));
        Assert.Equal(1, await MensajesEnElOutboxAsync());
    }
}
