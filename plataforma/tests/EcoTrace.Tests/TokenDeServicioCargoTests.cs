using System.Net;
using EcoTrace.CargoTracking.Api.Security;
using EcoTrace.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EcoTrace.Tests;

/// <summary>
/// El token de servicio con el que Cargo llama a Billing (ADR 0004). En el Trabajo 2 los servicios se llamaban sin identidad;
/// ahora cada llamada lleva un token de pocos minutos y para un solo módulo. Estas pruebas miran al proveedor que lo pide y
/// lo reutiliza, y al Outbox que lo usa de verdad.
/// </summary>
public sealed class TokenDeServicioCargoTests
{
    /// <summary>Un reloj que solo avanza cuando la prueba lo manda. Permite probar «pasaron 4 minutos» sin esperar.</summary>
    private sealed class RelojManual : TimeProvider
    {
        /// <summary>La hora actual del reloj de prueba.</summary>
        private DateTimeOffset _ahora = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

        /// <summary>Devuelve la hora del reloj de prueba.</summary>
        public override DateTimeOffset GetUtcNow() => _ahora;

        /// <summary>Adelanta el reloj el tiempo indicado.</summary>
        public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
    }

    /// <summary>Una fábrica de clientes HTTP que siempre entrega un cliente conectado al Identity de mentira.</summary>
    private sealed class FabricaDeUnCliente(HttpMessageHandler manejador) : IHttpClientFactory
    {
        /// <summary>Crea un cliente conectado al Identity de mentira.</summary>
        public HttpClient CreateClient(string name) =>
            new(manejador, disposeHandler: false) { BaseAddress = new Uri("http://identity.prueba") };
    }

    /// <summary>Arma el proveedor de tokens con un Identity de mentira y un reloj manual. Se pueden cambiar las credenciales para probar los fallos.</summary>
    private static (ProveedorDeTokenDeServicio Proveedor, IdentityFalso Identity, RelojManual Reloj) Crear(
        string clientId = "cargo", string secreto = SeguridadDePrueba.SecretoCargo)
    {
        var reloj = new RelojManual();
        var identity = new IdentityFalso { Reloj = reloj };
        var opciones = Options.Create(new OpcionesClienteServicio { ClientId = clientId, ClientSecret = secreto });
        var proveedor = new ProveedorDeTokenDeServicio(
            new FabricaDeUnCliente(identity), opciones, reloj, NullLogger<ProveedorDeTokenDeServicio>.Instance);
        return (proveedor, identity, reloj);
    }

    /// <summary>Pide un token para Billing dos veces seguidas: Identity solo lo entrega una vez y la segunda se reutiliza.</summary>
    [Fact]
    public async Task Pide_un_token_para_el_modulo_de_destino_y_lo_reutiliza()
    {
        var (proveedor, identity, _) = Crear();

        var primero = await proveedor.ObtenerAsync(Audiencias.Billing, CancellationToken.None);
        var segundo = await proveedor.ObtenerAsync(Audiencias.Billing, CancellationToken.None);

        Assert.Equal(primero, segundo);
        Assert.Equal(1, identity.LlamadasToken);
        Assert.Equal(new[] { Audiencias.Billing }, identity.AudienciasPedidas);
    }

    /// <summary>Pasan 3 minutos y el token sigue vigente (dura 5): se reutiliza el mismo.</summary>
    [Fact]
    public async Task Sigue_usando_el_mismo_token_mientras_le_queda_tiempo()
    {
        var (proveedor, identity, reloj) = Crear();
        var primero = await proveedor.ObtenerAsync(Audiencias.Billing, CancellationToken.None);

        reloj.Avanzar(TimeSpan.FromMinutes(3));
        var despues = await proveedor.ObtenerAsync(Audiencias.Billing, CancellationToken.None);

        Assert.Equal(primero, despues);
        Assert.Equal(1, identity.LlamadasToken);
    }

    /// <summary>Cuando al token le queda menos de un minuto, el proveedor pide uno nuevo para no mandar uno que venza en el camino.</summary>
    [Fact]
    public async Task Pide_otro_cuando_al_token_le_queda_menos_de_un_minuto()
    {
        var (proveedor, identity, reloj) = Crear();
        var primero = await proveedor.ObtenerAsync(Audiencias.Billing, CancellationToken.None);

        // El token dura 5 minutos: a los 4 y medio le quedan 30 segundos, menos del margen de un minuto.
        reloj.Avanzar(TimeSpan.FromSeconds(270));
        var nuevo = await proveedor.ObtenerAsync(Audiencias.Billing, CancellationToken.None);

        Assert.NotEqual(primero, nuevo);
        Assert.Equal(2, identity.LlamadasToken);
    }

    /// <summary>Cada módulo de destino necesita su propio token: pedir uno para Billing y otro para Fleet son dos pedidos distintos.</summary>
    [Fact]
    public async Task Cada_modulo_de_destino_recibe_su_propio_token()
    {
        var (proveedor, identity, _) = Crear();

        var paraBilling = await proveedor.ObtenerAsync(Audiencias.Billing, CancellationToken.None);
        var paraFleet = await proveedor.ObtenerAsync(Audiencias.Fleet, CancellationToken.None);

        Assert.NotEqual(paraBilling, paraFleet);
        Assert.Equal(2, identity.LlamadasToken);
    }

    /// <summary>Sin nombre ni contraseña del servicio, no se llama a Identity y la solicitud falla: sin identidad no se llama.</summary>
    [Fact]
    public async Task Sin_credenciales_no_pide_nada_y_falla()
    {
        var (proveedor, identity, _) = Crear(clientId: string.Empty, secreto: string.Empty);

        await Assert.ThrowsAsync<HttpRequestException>(() => proveedor.ObtenerAsync(Audiencias.Billing, CancellationToken.None));

        Assert.Equal(0, identity.LlamadasToken);
    }

    /// <summary>Si Identity rechaza la contraseña del servicio, la solicitud falla.</summary>
    [Fact]
    public async Task Si_Identity_rechaza_las_credenciales_la_llamada_falla()
    {
        var (proveedor, _, _) = Crear(secreto: "un-secreto-equivocado");

        await Assert.ThrowsAsync<HttpRequestException>(() => proveedor.ObtenerAsync(Audiencias.Billing, CancellationToken.None));
    }

    /// <summary>Si Identity no responde, la solicitud falla: no se inventa un token.</summary>
    [Fact]
    public async Task Si_Identity_no_responde_la_llamada_falla_y_no_se_inventa_un_token()
    {
        var (proveedor, identity, _) = Crear();
        identity.Caido = true;

        await Assert.ThrowsAsync<HttpRequestException>(() => proveedor.ObtenerAsync(Audiencias.Billing, CancellationToken.None));
    }

    // ------------------------------------------------------------------ el Outbox, con los cuatro servicios

    /// <summary>Con los cuatro servicios: se publican dos entregas hacia Billing y el token se pide una sola vez, para la audiencia correcta.</summary>
    [Fact]
    public async Task El_Outbox_pide_un_token_para_Billing_una_sola_vez_y_lo_reutiliza()
    {
        await using var ecosistema = await Ecosistema.IniciarAsync();
        var uno = await ecosistema.PrepararEnvioEnTransitoAsync();
        var dos = await ecosistema.PrepararEnvioEnTransitoAsync();
        await ecosistema.EntregarAsync(uno);
        await ecosistema.EntregarAsync(dos);

        var publicados = await ecosistema.PublicarOutboxAsync();

        Assert.Equal(2, publicados);
        Assert.Equal(new[] { Audiencias.Billing }, ecosistema.IdentityDeCargo.AudienciasPedidas);
        Assert.Equal(1, ecosistema.IdentityDeCargo.LlamadasToken);
    }

    /// <summary>Si Identity no entrega el token, el mensaje para Billing espera en el Outbox (no se pierde); cuando Identity vuelve, sale.</summary>
    [Fact]
    public async Task Si_Identity_no_entrega_el_token_el_mensaje_queda_pendiente_y_no_se_pierde()
    {
        await using var ecosistema = await Ecosistema.IniciarAsync();
        var envio = await ecosistema.PrepararEnvioEnTransitoAsync();
        await ecosistema.EntregarAsync(envio);

        // Sin identidad no se llama a Billing: el mensaje espera, no se descarta.
        ecosistema.IdentityDeCargo.Caido = true;
        Assert.Equal(0, await ecosistema.PublicarOutboxAsync());
        var pendiente = (await ecosistema.ClienteCargo.ConsultarAsync("/api/outbox")).Json[0];
        Assert.Equal("Pendiente", pendiente.GetProperty("estado").GetString());

        // Vuelve Identity y el mismo mensaje sale.
        ecosistema.IdentityDeCargo.Caido = false;
        Assert.Equal(1, await ecosistema.PublicarOutboxAsync());
        Assert.Equal(HttpStatusCode.OK, (await ecosistema.ClienteCargo.ConsultarAsync("/api/outbox")).Estado);
    }
}
