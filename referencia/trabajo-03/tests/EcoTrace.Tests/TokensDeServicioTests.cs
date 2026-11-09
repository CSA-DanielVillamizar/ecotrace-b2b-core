using EcoTrace.Tests.TestSupport;
using Microsoft.IdentityModel.JsonWebTokens;

namespace EcoTrace.Tests;

/// <summary>
/// Las llamadas entre servicios llevan identidad (ADR 0004): Billing llama a Identity y a Fleet, y Cargo llama a Billing, cada
/// una con un token de servicio emitido para el módulo de destino. Si Identity no lo entrega, no se llama: para el dinero eso
/// es fallar cerrado, y para el Outbox es dejar el mensaje pendiente.
/// </summary>
public sealed class TokensDeServicioTests : IAsyncLifetime
{
    private static readonly JsonWebTokenHandler Manejador = new();
    private Ecosistema _e = null!;

    public async Task InitializeAsync() => _e = await Ecosistema.IniciarAsync();

    public async Task DisposeAsync() => await _e.DisposeAsync();

    private static JsonWebToken Leer(string? autorizacion)
    {
        Assert.NotNull(autorizacion);
        Assert.StartsWith("Bearer ", autorizacion);
        return Manejador.ReadJsonWebToken(autorizacion["Bearer ".Length..]);
    }

    [Fact]
    public async Task Cada_llamada_del_Saga_y_del_Outbox_lleva_un_token_de_servicio_para_su_modulo_de_destino()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();

        await _e.EntregarYProcesarAsync(envio);

        Assert.Equal("Completada", (await _e.SagaDelPagoAsync(envio.PagoId!.Value)).Texto("estado"));
        Assert.NotEmpty(_e.IdentityDesdeBilling.Autorizaciones);
        Assert.NotEmpty(_e.FleetDesdeBilling.Autorizaciones);
        Assert.NotEmpty(_e.BillingDesdeCargo.Autorizaciones);

        foreach (var (cable, destino, cliente) in new[]
        {
            (_e.IdentityDesdeBilling, SeguridadDePrueba.AudIdentity, "billing"),
            (_e.FleetDesdeBilling, SeguridadDePrueba.AudFleet, "billing"),
            (_e.BillingDesdeCargo, SeguridadDePrueba.AudBilling, "cargo")
        })
        {
            foreach (var autorizacion in cable.Autorizaciones)
            {
                var token = Leer(autorizacion);
                Assert.Equal("Servicio", token.GetClaim("role").Value);
                Assert.Equal(cliente, token.Subject);
                // Un solo módulo de destino: el token robado de una llamada no sirve contra otro módulo.
                Assert.Equal([destino], token.Audiences);
                Assert.False(token.TryGetClaim("tenant_id", out _));
            }
        }
    }

    [Fact]
    public async Task El_token_de_servicio_se_reutiliza_y_no_se_pide_a_Identity_en_cada_llamada()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();

        await _e.EntregarYProcesarAsync(envio);

        // Billing llamó a Identity y a Fleet varias veces (autorizar, liberar, revocar...), pero pidió un token por módulo.
        Assert.True(_e.IdentityDesdeBilling.Llamadas + _e.FleetDesdeBilling.Llamadas >= 3);
        Assert.Equal(2, _e.IdentityTokensDesdeBilling.Llamadas);
        Assert.Equal(1, _e.IdentityTokensDesdeCargo.Llamadas);
    }

    [Fact]
    public async Task Si_Identity_no_entrega_el_token_de_servicio_el_Saga_falla_cerrado_sin_mover_dinero()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        _e.IdentityTokensDesdeBilling.Caido = true;

        await _e.EntregarYProcesarAsync(envio);

        var saga = await _e.SagaDelPagoAsync(envio.PagoId!.Value);
        Assert.Equal("Fallida", saga.Texto("estado"));
        Assert.Equal("EnCustodia", (await _e.PagoAsync(envio.PagoId.Value)).Propiedad("pago").GetProperty("estadoEscrow").GetString());

        // Sin identidad, ni siquiera se intentó llamar a Identity ni a Fleet: la llamada no salió.
        Assert.Equal(0, _e.IdentityDesdeBilling.Llamadas);
        Assert.Equal(0, _e.FleetDesdeBilling.Llamadas);
    }

    [Fact]
    public async Task Si_Identity_no_entrega_el_token_de_servicio_el_Outbox_conserva_el_evento_pendiente()
    {
        var envio = await _e.PrepararEnvioEnTransitoAsync();
        _e.IdentityTokensDesdeCargo.Caido = true;
        await _e.EntregarAsync(envio);

        var publicados = await _e.PublicarOutboxAsync();

        Assert.Equal(0, publicados);
        var mensaje = (await _e.ClienteCargo.ConsultarAsync("/api/outbox")).Json[0];
        Assert.Equal("Pendiente", mensaje.GetProperty("estado").GetString());
        Assert.Equal(0, _e.BillingDesdeCargo.Llamadas);

        // Identity vuelve: el evento sale y el Saga corre. No se perdió nada.
        _e.IdentityTokensDesdeCargo.Caido = false;
        Assert.Equal(1, await _e.PublicarOutboxAsync());
        await _e.AvanzarSagasAsync();
        Assert.Equal("Liberado", (await _e.PagoAsync(envio.PagoId!.Value)).Propiedad("pago").GetProperty("estadoEscrow").GetString());
    }
}
