using System.Collections.Concurrent;
using System.Net;
using EcoTrace.Billing.Api.Saga;
using EcoTrace.CargoTracking.Api.Outbox;
using EcoTrace.FleetManagement.Api.Extensions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EcoTrace.Tests.TestSupport;

/// <summary>
/// Cable entre dos servicios en las pruebas. Deja pasar la llamada al servicio de destino, que corre
/// en memoria, pero se puede cortar, hacer lento o dejar caer a partir de la n-ésima llamada para
/// reproducir los fallos que el Saga tiene que sobrevivir.
/// </summary>
public sealed class EnlaceDeRed(Func<HttpMessageHandler> destino) : HttpMessageHandler
{
    private readonly Lazy<HttpMessageInvoker> _invocador = new(() => new HttpMessageInvoker(destino(), disposeHandler: false));
    private readonly ConcurrentQueue<string> _registro = new();
    private readonly ConcurrentQueue<string?> _autorizaciones = new();
    private int _llamadas;

    /// <summary>Mientras sea true, toda llamada falla como si no hubiera conexión.</summary>
    public bool Caido { get; set; }

    /// <summary>Número de llamada (contando desde 1) a partir del cual el enlace cae.</summary>
    public int CaidoDesdeLlamada { get; set; } = int.MaxValue;

    /// <summary>Espera antes de reenviar la llamada. Sirve para provocar un tiempo agotado.</summary>
    public TimeSpan Retraso { get; set; }

    public int Llamadas => Volatile.Read(ref _llamadas);

    public IReadOnlyCollection<string> Registro => _registro;

    /// <summary>El encabezado Authorization de cada llamada que pasó por el cable, en orden. Nulo si iba sin identidad.</summary>
    public IReadOnlyCollection<string?> Autorizaciones => _autorizaciones;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        var numero = Interlocked.Increment(ref _llamadas);
        _registro.Enqueue($"{solicitud.Method} {solicitud.RequestUri?.AbsolutePath}");
        _autorizaciones.Enqueue(solicitud.Headers.Authorization?.ToString());

        if (Caido || numero >= CaidoDesdeLlamada)
        {
            throw new HttpRequestException("Enlace caído (simulado en la prueba).");
        }

        if (Retraso > TimeSpan.Zero)
        {
            await Task.Delay(Retraso, ct);
        }

        return await _invocador.Value.SendAsync(solicitud, ct);
    }

    protected override void Dispose(bool disposing)
    {
        // El destino lo libera quien lo creó: este cable solo lo usa.
    }
}

public sealed record OpcionesEcosistema(int TimeoutIdentityMs = 10_000, int OutboxMaximoIntentos = 3);

/// <summary>Los identificadores de un envío ya preparado: a punto de entregarse y con su pago en custodia.</summary>
public sealed record Escenario(
    Guid GeneradorId, Guid TransportistaId, Guid VehiculoId, Guid ConductorId, Guid CargaId, Guid? PagoId)
{
    /// <summary>Administrador de la organización generadora: pide la carga, paga y libera el pago.</summary>
    public Persona AdminGenerador { get; init; } = null!;

    /// <summary>Supervisor del transportista: registra la flota, asigna la carga y registra el recorrido.</summary>
    public Persona SupervisorTransportista { get; init; } = null!;

    /// <summary>Conductor del transportista: confirma la entrega.</summary>
    public Persona ConductorTransportista { get; init; } = null!;

    /// <summary>Administrador del transportista.</summary>
    public Persona AdminTransportista { get; init; } = null!;
}

/// <summary>
/// Los cuatro servicios reales, en memoria, con su propia base SQLite cada uno y hablándose por HTTP
/// a través de cables que la prueba controla. Es lo más cercano a docker compose sin puertos ni contenedores.
/// Los ejecutores automáticos están apagados: la prueba decide cuándo corre el publicador del Outbox y
/// cuándo avanza el Saga, y así puede mirar el estado entre un paso y otro.
/// </summary>
public sealed class Ecosistema : IAsyncDisposable
{
    private readonly ApiFactory<IdentityApiMarker> _identityBase = new();
    private readonly ApiFactory<FleetManagementApiMarker> _fleetBase = new();
    private readonly ApiFactory<CargoTrackingApiMarker> _cargoBase = new();
    private readonly ApiFactory<BillingApiMarker> _billingBase = new();

    private Ecosistema(OpcionesEcosistema opciones)
    {
        Identity = _identityBase;
        // Cables aparte para la seguridad (JWKS y tokens de servicio): los de negocio se cortan o se atrasan en las
        // pruebas del Saga y no deben contar las llamadas de seguridad.
        IdentitySeguridadDesdeFleet = new EnlaceDeRed(() => Identity.Server.CreateHandler());
        IdentitySeguridadDesdeCargo = new EnlaceDeRed(() => Identity.Server.CreateHandler());
        IdentitySeguridadDesdeBilling = new EnlaceDeRed(() => Identity.Server.CreateHandler());
        IdentityTokensDesdeCargo = new EnlaceDeRed(() => Identity.Server.CreateHandler());
        IdentityTokensDesdeBilling = new EnlaceDeRed(() => Identity.Server.CreateHandler());

        Fleet = _fleetBase.WithWebHostBuilder(b =>
        {
            b.UseSetting("Simulacion:Habilitada", "true");
            b.ConfigureTestServices(servicios =>
                servicios.AddHttpClient(ClienteJwks).ConfigurePrimaryHttpMessageHandler(() => IdentitySeguridadDesdeFleet));
        });

        IdentityDesdeBilling = new EnlaceDeRed(() => Identity.Server.CreateHandler());
        FleetDesdeBilling = new EnlaceDeRed(() => Fleet.Server.CreateHandler());
        BillingDesdeCargo = new EnlaceDeRed(() => Billing!.Server.CreateHandler()); // Billing se asigna más abajo; el cable lo usa recién en la primera llamada.

        Billing = _billingBase.WithWebHostBuilder(b =>
        {
            b.UseSetting("Saga:EjecutorAutomatico", "false");
            b.UseSetting("Saga:EsperaBaseSegundos", "0");
            b.UseSetting("Resiliencia:Identity:EsperaBaseMs", "0");
            b.UseSetting("Resiliencia:Identity:TimeoutMs", opciones.TimeoutIdentityMs.ToString());
            b.UseSetting("Servicio:ClientId", "billing");
            b.UseSetting("Servicio:ClientSecret", SeguridadDePrueba.SecretoBilling);
            b.ConfigureTestServices(servicios =>
            {
                servicios.AddHttpClient(ClienteJwks).ConfigurePrimaryHttpMessageHandler(() => IdentitySeguridadDesdeBilling);
                servicios.AddHttpClient(ClienteToken).ConfigurePrimaryHttpMessageHandler(() => IdentityTokensDesdeBilling);
                servicios.AddHttpClient(ServiciosExternos.Identity).ConfigurePrimaryHttpMessageHandler(() => IdentityDesdeBilling);
                servicios.AddHttpClient(ServiciosExternos.Fleet).ConfigurePrimaryHttpMessageHandler(() => FleetDesdeBilling);
            });
        });

        Cargo = _cargoBase.WithWebHostBuilder(b =>
        {
            b.UseSetting("Outbox:EjecutorAutomatico", "false");
            b.UseSetting("Outbox:EsperaBaseSegundos", "0");
            b.UseSetting("Outbox:MaximoIntentos", opciones.OutboxMaximoIntentos.ToString());
            b.UseSetting("Servicio:ClientId", "cargo");
            b.UseSetting("Servicio:ClientSecret", SeguridadDePrueba.SecretoCargo);
            b.ConfigureTestServices(servicios =>
            {
                servicios.AddHttpClient(OutboxPublicador.ClienteBilling).ConfigurePrimaryHttpMessageHandler(() => BillingDesdeCargo);
                servicios.AddHttpClient(ClienteJwks).ConfigurePrimaryHttpMessageHandler(() => IdentitySeguridadDesdeCargo);
                servicios.AddHttpClient(ClienteToken).ConfigurePrimaryHttpMessageHandler(() => IdentityTokensDesdeCargo);
            });
        });

        ClienteIdentity = Identity.CreateClient();
        ClienteFleet = Fleet.CreateClient();
        ClienteCargo = Cargo.CreateClient();
        ClienteBilling = Billing.CreateClient();
        Actuar(Actor.Operador);
    }

    private const string ClienteJwks = "identity-jwks";
    private const string ClienteToken = "identity-token";

    /// <summary>Cables de cada servicio hacia Identity para bajar el JWKS y pedir tokens de servicio.</summary>
    public EnlaceDeRed IdentitySeguridadDesdeFleet { get; }

    public EnlaceDeRed IdentitySeguridadDesdeCargo { get; }

    public EnlaceDeRed IdentitySeguridadDesdeBilling { get; }

    /// <summary>Cables con los que Cargo y Billing piden a Identity sus tokens de servicio.</summary>
    public EnlaceDeRed IdentityTokensDesdeCargo { get; }

    public EnlaceDeRed IdentityTokensDesdeBilling { get; }

    /// <summary>Quien hace las llamadas de los cuatro clientes en este momento. Cambia con <see cref="Actuar"/>.</summary>
    public Actor ActorActual { get; private set; } = Actor.Operador;

    /// <summary>Desde aquí, las llamadas de los cuatro clientes llevan el token de este actor.</summary>
    public void Actuar(Actor actor)
    {
        ActorActual = actor;
        Poner(ClienteIdentity, actor, SeguridadDePrueba.AudIdentity);
        Poner(ClienteFleet, actor, SeguridadDePrueba.AudFleet);
        Poner(ClienteCargo, actor, SeguridadDePrueba.AudCargo);
        Poner(ClienteBilling, actor, SeguridadDePrueba.AudBilling);

        static void Poner(HttpClient cliente, Actor actor, string audiencia)
        {
            var token = actor.TokenPara(audiencia);
            cliente.ConToken(string.IsNullOrEmpty(token) ? null : token);
        }
    }

    /// <summary>
    /// Una llamada con el token de otro actor, sin cambiar el actor de los clientes. Sirve para las llamadas entre servicios
    /// que una prueba hace a mano (y se pueden hacer varias a la vez sin pisarse).
    /// </summary>
    public async Task<Respuesta> LlamarComoAsync(Actor actor, HttpClient cliente, string audiencia, HttpMethod metodo, string ruta, object? cuerpo = null)
    {
        using var solicitud = new HttpRequestMessage(metodo, ruta);
        solicitud.Headers.Authorization = new("Bearer", actor.TokenPara(audiencia));
        if (cuerpo is not null)
        {
            solicitud.Content = System.Net.Http.Json.JsonContent.Create(cuerpo);
        }

        return await Respuesta.De(await cliente.SendAsync(solicitud));
    }

    /// <summary>Lo que Identity sabe de la autorización de un pago. Es una ruta interna: solo la ve un servicio.</summary>
    public Task<Respuesta> AutorizacionDelPagoAsync(Guid pagoId) =>
        LlamarComoAsync(Actor.ServicioInterno, ClienteIdentity, SeguridadDePrueba.AudIdentity, HttpMethod.Get, $"/api/autorizaciones-pago/{pagoId}");

    /// <summary>Entrega a Billing un evento EntregaConfirmada, como lo hace el publicador del Outbox de Cargo.</summary>
    public Task<Respuesta> EntregaConfirmadaAsync(object evento) =>
        LlamarComoAsync(Actor.ServicioInterno, ClienteBilling, SeguridadDePrueba.AudBilling, HttpMethod.Post, "/api/eventos/entrega-confirmada", evento);

    /// <summary>Cambia de actor solo hasta que se libere el resultado: al terminar vuelve al anterior.</summary>
    public IDisposable ActuandoComo(Actor actor)
    {
        var anterior = ActorActual;
        Actuar(actor);
        return new Restaurador(() => Actuar(anterior));
    }

    private sealed class Restaurador(Action alLiberar) : IDisposable
    {
        public void Dispose() => alLiberar();
    }

    public WebApplicationFactory<IdentityApiMarker> Identity { get; }

    public WebApplicationFactory<FleetManagementApiMarker> Fleet { get; }

    public WebApplicationFactory<CargoTrackingApiMarker> Cargo { get; }

    public WebApplicationFactory<BillingApiMarker> Billing { get; }

    public HttpClient ClienteIdentity { get; }

    public HttpClient ClienteFleet { get; }

    public HttpClient ClienteCargo { get; }

    public HttpClient ClienteBilling { get; }

    /// <summary>Cable de Billing hacia Identity (autorizar, verificar y revocar la autorización).</summary>
    public EnlaceDeRed IdentityDesdeBilling { get; }

    /// <summary>Cable de Billing hacia Fleet Management (liberar recursos).</summary>
    public EnlaceDeRed FleetDesdeBilling { get; }

    /// <summary>Cable de Cargo &amp; Tracking hacia Billing (publicar EntregaConfirmada).</summary>
    public EnlaceDeRed BillingDesdeCargo { get; }

    public SimulacionFallos FallosDeFleet => Fleet.Services.GetRequiredService<SimulacionFallos>();

    public OutboxPublicador PublicadorDeCargo => Cargo.Services.GetRequiredService<OutboxPublicador>();

    public SagaEjecutor EjecutorDeBilling => Billing.Services.GetRequiredService<SagaEjecutor>();

    public static async Task<Ecosistema> IniciarAsync(OpcionesEcosistema? opciones = null)
    {
        var ecosistema = new Ecosistema(opciones ?? new OpcionesEcosistema());

        // Arranca los cuatro y aplica sus migraciones antes de la prueba, para que la primera llamada
        // real no cargue con el costo de arranque y agote un tiempo de espera ajustado.
        foreach (var cliente in new[] { ecosistema.ClienteIdentity, ecosistema.ClienteFleet, ecosistema.ClienteCargo, ecosistema.ClienteBilling })
        {
            (await cliente.GetAsync("/health")).EnsureSuccessStatusCode();
        }

        return ecosistema;
    }

    /// <summary>
    /// Deja un envío listo para entregarse: organizaciones y flota registradas, carga asignada con sus
    /// recursos reservados y en tránsito, y el pago en custodia. Todo por las APIs, sin tocar bases de datos.
    /// </summary>
    public async Task<Escenario> PrepararEnvioEnTransitoAsync(bool conPago = true, decimal monto = 850_000m)
    {
        var sufijo = Guid.NewGuid().ToString("N");
        var actorAnterior = ActorActual;
        Actuar(Actor.Operador);
        var generador = await ClienteIdentity.PostearAsync("/api/tenants", new { nombre = $"Generadora {sufijo[..6]}", tenantType = "Generador" });
        var transportista = await ClienteIdentity.PostearAsync("/api/tenants", new { nombre = $"Transportes {sufijo[..6]}", tenantType = "Transportista" });
        var generadorId = generador.Id("tenantId");
        var transportistaId = transportista.Id("tenantId");

        var adminGenerador = new Persona(generadorId, Rol.Administrador);
        var supervisor = new Persona(transportistaId, Rol.Supervisor);
        var conductorDelTransportista = new Persona(transportistaId, Rol.Conductor);
        var adminTransportista = new Persona(transportistaId, Rol.Administrador);

        Actuar(supervisor);
        var vehiculo = await ClienteFleet.PostearAsync("/api/vehiculos", new
        {
            tenantId = transportistaId, registradoPorUserId = supervisor.UserId,
            placa = $"T{sufijo[..6].ToUpperInvariant()}", capacidadKg = 5000
        });
        var conductor = await ClienteFleet.PostearAsync("/api/conductores", new
        {
            tenantId = transportistaId, registradoPorUserId = supervisor.UserId,
            nombre = "Carlos Ruiz", licencia = $"C2-{sufijo[6..12]}"
        });

        Actuar(adminGenerador);
        var carga = await ClienteCargo.PostearAsync("/api/cargas", new
        {
            generadorTenantId = generadorId, transportistaTenantId = transportistaId,
            descripcion = "Residuos industriales no peligrosos", origen = "Cali", destino = "Popayán", pesoKg = 4200
        });
        var cargaId = carga.Id("cargaId");
        var vehiculoId = vehiculo.Id("vehiculoId");
        var conductorId = conductor.Id("conductorId");

        Actuar(supervisor);
        await ClienteCargo.PostearAsync($"/api/cargas/{cargaId}/asignacion", new { vehiculoId, conductorId });
        await ClienteCargo.PostearAsync($"/api/cargas/{cargaId}/seguimientos", new { estado = "EnTransito", ubicacion = "Santander de Quilichao" });

        // La reserva de los recursos es una llamada entre servicios (la hace Billing en el Saga): va con token de servicio.
        Actuar(Actor.ServicioInterno);
        var reserva = await ClienteFleet.PostearAsync("/api/reservas", new { cargaId, vehiculoId, conductorId });
        Assert.Equal(HttpStatusCode.Created, reserva.Estado);

        Guid? pagoId = null;
        if (conPago)
        {
            Actuar(adminGenerador);
            var pago = await ClienteBilling.PostearAsync("/api/pagos", new
            {
                generadorTenantId = generadorId, transportistaTenantId = transportistaId, cargaId, monto
            });
            pagoId = pago.Propiedad("pago").GetProperty("pagoId").GetGuid();
        }

        Actuar(actorAnterior);
        return new Escenario(generadorId, transportistaId, vehiculoId, conductorId, cargaId, pagoId)
        {
            AdminGenerador = adminGenerador,
            SupervisorTransportista = supervisor,
            ConductorTransportista = conductorDelTransportista,
            AdminTransportista = adminTransportista
        };
    }

    /// <summary>El conductor entrega la carga. Cargo &amp; Tracking registra la entrega y deja el evento en su Outbox.</summary>
    public async Task<Respuesta> EntregarAsync(Escenario envio)
    {
        using var _ = ActuandoComo(envio.ConductorTransportista);
        var respuesta = await ClienteCargo.PostearAsync(
            $"/api/cargas/{envio.CargaId}/seguimientos", new { estado = "Entregado", ubicacion = "Popayán" });
        Assert.Equal(HttpStatusCode.Created, respuesta.Estado);
        return respuesta;
    }

    /// <summary>Publica los mensajes pendientes del Outbox de Cargo & Tracking hacia Billing.</summary>
    public Task<int> PublicarOutboxAsync() => PublicadorDeCargo.PublicarPendientesAsync();

    /// <summary>Avanza los Sagas de Billing hasta que ninguno tenga trabajo vencido.</summary>
    public async Task AvanzarSagasAsync()
    {
        for (var vuelta = 0; vuelta < 30; vuelta++)
        {
            if (await EjecutorDeBilling.ProcesarPendientesAsync() == 0)
            {
                return;
            }
        }

        throw new InvalidOperationException("Los Sagas siguen activos tras 30 vueltas: algo se quedó girando.");
    }

    /// <summary>Entrega, publica el evento y corre el Saga de principio a fin.</summary>
    public async Task EntregarYProcesarAsync(Escenario envio)
    {
        await EntregarAsync(envio);
        await PublicarOutboxAsync();
        await AvanzarSagasAsync();
    }

    public async Task<Respuesta> SagaDelPagoAsync(Guid pagoId)
    {
        using var _ = ActuandoComo(Actor.Operador);
        var lista = await ClienteBilling.ConsultarAsync($"/api/sagas?pagoId={pagoId}");
        Assert.Equal(1, lista.Cantidad);
        return new Respuesta(lista.Estado, lista.Json[0]);
    }

    public async Task<Respuesta> PagoAsync(Guid pagoId)
    {
        using var _ = ActuandoComo(Actor.Operador);
        return await ClienteBilling.ConsultarAsync($"/api/pagos/{pagoId}");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var cliente in new[] { ClienteIdentity, ClienteFleet, ClienteCargo, ClienteBilling })
        {
            cliente.Dispose();
        }

        // Cada base libera las fábricas derivadas (las que llevan la configuración de la prueba) y su base SQLite.
        await _cargoBase.DisposeAsync();
        await _billingBase.DisposeAsync();
        await _fleetBase.DisposeAsync();
        await _identityBase.DisposeAsync();
    }
}
