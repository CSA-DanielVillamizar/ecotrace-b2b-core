using System.Collections.Concurrent;
using System.Net;
using EcoTrace.Billing.Api.Saga;
using EcoTrace.CargoTracking.Api.Outbox;
using EcoTrace.FleetManagement.Api.Extensions;
using EcoTrace.FleetManagement.Api.Seguridad;
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
    private int _llamadas;

    /// <summary>Mientras sea true, toda llamada falla como si no hubiera conexión.</summary>
    public bool Caido { get; set; }

    /// <summary>Número de llamada (contando desde 1) a partir del cual el enlace cae.</summary>
    public int CaidoDesdeLlamada { get; set; } = int.MaxValue;

    /// <summary>Espera antes de reenviar la llamada. Sirve para provocar un tiempo agotado.</summary>
    public TimeSpan Retraso { get; set; }

    public int Llamadas => Volatile.Read(ref _llamadas);

    public IReadOnlyCollection<string> Registro => _registro;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage solicitud, CancellationToken ct)
    {
        var numero = Interlocked.Increment(ref _llamadas);
        _registro.Enqueue($"{solicitud.Method} {solicitud.RequestUri?.AbsolutePath}");

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
    Guid GeneradorId, Guid TransportistaId, Guid VehiculoId, Guid ConductorId, Guid CargaId, Guid? PagoId);

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
        Fleet = _fleetBase.WithWebHostBuilder(b =>
        {
            b.UseSetting("Simulacion:Habilitada", "true");
            // Fleet valida los tokens con el JWKS del Identity de la prueba (Trabajo 3).
            b.ConfigureTestServices(servicios =>
                servicios.AddHttpClient(SeguridadFleet.ClienteJwks).ConfigurePrimaryHttpMessageHandler(() => Identity.Server.CreateHandler()));
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
            b.ConfigureTestServices(servicios =>
            {
                // Billing llama a Identity con token de servicio (Trabajo 3): sin él, la ruta interna responde 401.
                servicios.AddHttpClient(ServiciosExternos.Identity)
                    .ConfigurePrimaryHttpMessageHandler(() => IdentityDesdeBilling)
                    .AddHttpMessageHandler(() => new TokenDeServicioHandler(
                        () => Identity.CreateClient(), "billing", Acceso.SecretoBilling, "ecotrace-identity"));
                // Lo mismo hacia Fleet: sus rutas internas (reservas y liberaciones) también exigen token de servicio.
                servicios.AddHttpClient(ServiciosExternos.Fleet)
                    .ConfigurePrimaryHttpMessageHandler(() => FleetDesdeBilling)
                    .AddHttpMessageHandler(() => new TokenDeServicioHandler(
                        () => Identity.CreateClient(), "billing", Acceso.SecretoBilling, "ecotrace-fleet"));
            });
        });

        Cargo = _cargoBase.WithWebHostBuilder(b =>
        {
            b.UseSetting("Outbox:EjecutorAutomatico", "false");
            b.UseSetting("Outbox:EsperaBaseSegundos", "0");
            b.UseSetting("Outbox:MaximoIntentos", opciones.OutboxMaximoIntentos.ToString());
            b.ConfigureTestServices(servicios =>
                servicios.AddHttpClient(OutboxPublicador.ClienteBilling).ConfigurePrimaryHttpMessageHandler(() => BillingDesdeCargo));
        });

        ClienteIdentity = Identity.CreateClient();
        ClienteIdentityComoBilling = Identity.CreateClient();
        ClienteFleet = Fleet.CreateClient();
        ClienteCargo = Cargo.CreateClient();
        ClienteBilling = Billing.CreateClient();
    }

    public WebApplicationFactory<IdentityApiMarker> Identity { get; }

    public WebApplicationFactory<FleetManagementApiMarker> Fleet { get; }

    public WebApplicationFactory<CargoTrackingApiMarker> Cargo { get; }

    public WebApplicationFactory<BillingApiMarker> Billing { get; }

    /// <summary>Identity con token del administrador de la plataforma: crea organizaciones y usuarios, suspende.</summary>
    public HttpClient ClienteIdentity { get; }

    /// <summary>Identity con token de servicio de Billing: el que usa las rutas internas (autorizaciones de pago).</summary>
    public HttpClient ClienteIdentityComoBilling { get; }

    /// <summary>Fleet con token de servicio de Billing: reserva, libera y consulta recursos como lo hace el Saga.</summary>
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

        ecosistema.ClienteIdentity.ConToken(await ecosistema.ClienteIdentity.TokenAdministradorAsync());
        ecosistema.ClienteIdentityComoBilling.ConToken(await ecosistema.ClienteIdentityComoBilling.TokenServicioAsync(
            "billing", Acceso.SecretoBilling, "ecotrace-identity"));
        ecosistema.ClienteFleet.ConToken(await ecosistema.ClienteIdentity.TokenServicioAsync(
            "billing", Acceso.SecretoBilling, "ecotrace-fleet"));

        return ecosistema;
    }

    /// <summary>
    /// Deja un envío listo para entregarse: organizaciones y flota registradas, carga asignada con sus
    /// recursos reservados y en tránsito, y el pago en custodia. Todo por las APIs, sin tocar bases de datos.
    /// </summary>
    public async Task<Escenario> PrepararEnvioEnTransitoAsync(bool conPago = true, decimal monto = 850_000m)
    {
        var sufijo = Guid.NewGuid().ToString("N");
        var generador = await ClienteIdentity.PostearAsync("/api/tenants", new { nombre = $"Generadora {sufijo[..6]}", tenantType = "Generador" });
        var transportista = await ClienteIdentity.PostearAsync("/api/tenants", new { nombre = $"Transportes {sufijo[..6]}", tenantType = "Transportista" });
        var emailSupervisora = $"marta.{sufijo}@ejemplo.co";
        await ClienteIdentity.PostearAsync("/api/users", new
        {
            tenantId = transportista.Id("tenantId"), role = "Supervisor", nombre = "Marta Ríos", email = emailSupervisora,
            password = "Supervisora-2026"
        });
        var generadorId = generador.Id("tenantId");
        var transportistaId = transportista.Id("tenantId");

        // La supervisora registra la flota con su propio token: Fleet toma la organización de ahí (Trabajo 3).
        using var fleetSupervisora = Fleet.CreateClient().ConToken(await ClienteIdentity.TokenDeAsync(emailSupervisora, "Supervisora-2026"));
        var vehiculo = await fleetSupervisora.PostearAsync("/api/vehiculos", new
        {
            placa = $"T{sufijo[..6].ToUpperInvariant()}", capacidadKg = 5000
        });
        var conductor = await fleetSupervisora.PostearAsync("/api/conductores", new
        {
            nombre = "Carlos Ruiz", licencia = $"C2-{sufijo[6..12]}"
        });

        var carga = await ClienteCargo.PostearAsync("/api/cargas", new
        {
            generadorTenantId = generadorId, transportistaTenantId = transportistaId,
            descripcion = "Residuos industriales no peligrosos", origen = "Cali", destino = "Popayán", pesoKg = 4200
        });
        var cargaId = carga.Id("cargaId");
        var vehiculoId = vehiculo.Id("vehiculoId");
        var conductorId = conductor.Id("conductorId");

        await ClienteCargo.PostearAsync($"/api/cargas/{cargaId}/asignacion", new { vehiculoId, conductorId });
        var reserva = await ClienteFleet.PostearAsync("/api/reservas", new { cargaId, vehiculoId, conductorId });
        Assert.Equal(HttpStatusCode.Created, reserva.Estado);
        await ClienteCargo.PostearAsync($"/api/cargas/{cargaId}/seguimientos", new { estado = "EnTransito", ubicacion = "Santander de Quilichao" });

        Guid? pagoId = null;
        if (conPago)
        {
            var pago = await ClienteBilling.PostearAsync("/api/pagos", new
            {
                generadorTenantId = generadorId, transportistaTenantId = transportistaId, cargaId, monto
            });
            pagoId = pago.Propiedad("pago").GetProperty("pagoId").GetGuid();
        }

        return new Escenario(generadorId, transportistaId, vehiculoId, conductorId, cargaId, pagoId);
    }

    /// <summary>El conductor entrega la carga. Cargo &amp; Tracking registra la entrega y deja el evento en su Outbox.</summary>
    public async Task<Respuesta> EntregarAsync(Escenario envio)
    {
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
        var lista = await ClienteBilling.ConsultarAsync($"/api/sagas?pagoId={pagoId}");
        Assert.Equal(1, lista.Cantidad);
        return new Respuesta(lista.Estado, lista.Json[0]);
    }

    public async Task<Respuesta> PagoAsync(Guid pagoId) =>
        await ClienteBilling.ConsultarAsync($"/api/pagos/{pagoId}");

    public async ValueTask DisposeAsync()
    {
        foreach (var cliente in new[] { ClienteIdentity, ClienteIdentityComoBilling, ClienteFleet, ClienteCargo, ClienteBilling })
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
