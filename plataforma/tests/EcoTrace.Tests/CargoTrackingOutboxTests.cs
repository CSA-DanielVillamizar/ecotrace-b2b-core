using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EcoTrace.CargoTracking.Infrastructure;
using EcoTrace.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EcoTrace.Tests;

public sealed class CargoTrackingOutboxTests
{
    [Fact]
    public async Task Entregado_guarda_un_evento_pendiente_con_identificadores_y_correlacion()
    {
        using var fabrica = new CargoTrackingOutboxApiFactory();
        using var cliente = fabrica.CreateClient();
        var carga = await CrearCargaEnTransitoAsync(cliente);
        const string correlationId = "traza_entrega_01";

        using var solicitud = new HttpRequestMessage(HttpMethod.Post,
            $"/api/cargas/{carga.CargaId}/seguimientos")
        {
            Content = JsonContent.Create(new
            {
                estado = "Entregado",
                ubicacion = "Centro de acopio",
                nota = "Entrega verificada"
            })
        };
        solicitud.Headers.Add("X-Correlation-Id", correlationId);

        var entrega = await Respuesta.De(await cliente.SendAsync(solicitud));
        var mensajes = await cliente.ConsultarAsync("/api/outbox?estado=Pendiente");
        await using var db = await fabrica.Services
            .GetRequiredService<IDbContextFactory<CargoTrackingDbContext>>()
            .CreateDbContextAsync();
        var mensaje = await db.OutboxMessages.SingleAsync(m => m.CargaId == carga.CargaId);

        Assert.Equal(HttpStatusCode.Created, entrega.Estado);
        Assert.Equal(1, mensajes.Cantidad);
        Assert.Equal("EntregaConfirmada", mensajes.Json[0].GetProperty("eventType").GetString());
        Assert.Equal("Pendiente", mensajes.Json[0].GetProperty("estado").GetString());
        Assert.Equal(0, mensajes.Json[0].GetProperty("intentos").GetInt32());
        Assert.Equal(correlationId, mensaje.CorrelationId);
        Assert.Equal("EntregaConfirmada", mensaje.EventType);
        Assert.Equal(carga.CargaId, mensaje.CargaId);
        Assert.Equal(carga.VehiculoId, mensaje.VehiculoId);
        Assert.Equal(carga.ConductorId, mensaje.ConductorId);
        Assert.Equal(carga.GeneradorTenantId, mensaje.GeneradorTenantId);
        Assert.Equal(carga.TransportistaTenantId, mensaje.TransportistaTenantId);
        Assert.Equal("Pendiente", mensaje.Estado);
        Assert.Equal(Guid.Parse(mensajes.Json[0].GetProperty("eventId").GetString()!),
            Guid.Parse(mensaje.EventId));
    }

    [Fact]
    public async Task Genera_correlacion_cuando_el_header_es_invalido()
    {
        using var fabrica = new CargoTrackingOutboxApiFactory();
        using var cliente = fabrica.CreateClient();
        var carga = await CrearCargaEnTransitoAsync(cliente);
        using var solicitud = new HttpRequestMessage(HttpMethod.Post,
            $"/api/cargas/{carga.CargaId}/seguimientos")
        {
            Content = JsonContent.Create(new
            {
                estado = "Entregado",
                ubicacion = "Centro de acopio",
                nota = "Entrega verificada"
            })
        };
        solicitud.Headers.Add("X-Correlation-Id", "correlacion invalida!");

        Assert.Equal(HttpStatusCode.Created, (await cliente.SendAsync(solicitud)).StatusCode);
        var pendientes = await cliente.ConsultarAsync("/api/outbox?estado=Pendiente");
        await using var db = await fabrica.Services
            .GetRequiredService<IDbContextFactory<CargoTrackingDbContext>>()
            .CreateDbContextAsync();
        var mensaje = await db.OutboxMessages.SingleAsync(m => m.CargaId == carga.CargaId);

        Assert.Equal(1, pendientes.Cantidad);
        Assert.NotEqual("correlacion invalida!", mensaje.CorrelationId);
        Assert.True(Guid.TryParse(mensaje.CorrelationId, out _));
        Assert.InRange(mensaje.CorrelationId.Length, 1, 64);
        Assert.All(mensaje.CorrelationId,
            caracter => Assert.True(char.IsLetterOrDigit(caracter) || caracter is '-' or '_'));
    }

    [Fact]
    public async Task Solo_entregado_crea_un_evento_y_repetir_entregado_no_duplica()
    {
        using var fabrica = new CargoTrackingOutboxApiFactory();
        using var cliente = fabrica.CreateClient();
        var carga = await CrearCargaEnTransitoAsync(cliente);

        Assert.Equal(0, (await cliente.ConsultarAsync("/api/outbox")).Cantidad);
        Assert.Equal(HttpStatusCode.Created,
            (await cliente.PostearAsync($"/api/cargas/{carga.CargaId}/seguimientos", new
            {
                estado = "Entregado",
                ubicacion = "Centro de acopio",
                nota = "Entrega verificada"
            })).Estado);
        Assert.Equal(HttpStatusCode.Conflict,
            (await cliente.PostearAsync($"/api/cargas/{carga.CargaId}/seguimientos", new
            {
                estado = "Entregado",
                ubicacion = "Centro de acopio",
                nota = "Repetición"
            })).Estado);

        var mensajes = await cliente.ConsultarAsync("/api/outbox");
        Assert.Equal(1, mensajes.Cantidad);
        Assert.Equal(carga.CargaId, mensajes.Json[0].GetProperty("cargaId").GetGuid());
    }

    [Fact]
    public async Task Dos_entregas_simultaneas_crean_un_solo_evento()
    {
        using var fabrica = new CargoTrackingOutboxApiFactory();
        using var cliente = fabrica.CreateClient();
        var carga = await CrearCargaEnTransitoAsync(cliente);
        object Solicitud() => new
        {
            estado = "Entregado",
            ubicacion = "Centro de acopio",
            nota = "Entrega verificada"
        };

        var respuestas = await Task.WhenAll(
            cliente.PostearAsync($"/api/cargas/{carga.CargaId}/seguimientos", Solicitud()),
            cliente.PostearAsync($"/api/cargas/{carga.CargaId}/seguimientos", Solicitud()));
        var mensajes = await cliente.ConsultarAsync("/api/outbox");

        Assert.Single(respuestas, r => r.Estado == HttpStatusCode.Created);
        Assert.Single(respuestas, r => r.Estado == HttpStatusCode.Conflict);
        Assert.Single(mensajes.Json.EnumerateArray());
    }

    [Fact]
    public async Task Un_fallo_al_guardar_el_outbox_revierte_entrega_y_seguimiento()
    {
        using var fabrica = new CargoTrackingOutboxApiFactory();
        using var cliente = fabrica.CreateClient();
        var carga = await CrearCargaEnTransitoAsync(cliente);
        await using (var db = await fabrica.Services
            .GetRequiredService<IDbContextFactory<CargoTrackingDbContext>>()
            .CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER FallarInsertOutbox
                BEFORE INSERT ON OutboxMessages
                BEGIN
                    SELECT RAISE(ABORT, 'fallo de persistencia simulado');
                END;
                """);
        }

        var entrega = await cliente.PostearAsync($"/api/cargas/{carga.CargaId}/seguimientos", new
        {
            estado = "Entregado",
            ubicacion = "Centro de acopio",
            nota = "Entrega verificada"
        });
        var cargaPersistida = await cliente.ConsultarAsync($"/api/cargas/{carga.CargaId}");
        var seguimientos = await cliente.ConsultarAsync($"/api/cargas/{carga.CargaId}/seguimientos");
        var mensajes = await cliente.ConsultarAsync("/api/outbox");

        Assert.Equal(HttpStatusCode.InternalServerError, entrega.Estado);
        Assert.Equal("EnTransito", cargaPersistida.Propiedad("carga").GetProperty("estado").GetString());
        Assert.Equal(2, seguimientos.Cantidad);
        Assert.Equal(0, mensajes.Cantidad);
    }

    [Fact]
    public async Task Lista_por_estado_y_reprocesa_un_mensaje_muerto_reiniciando_intentos()
    {
        using var fabrica = new CargoTrackingOutboxApiFactory();
        using var cliente = fabrica.CreateClient();
        var carga = await CrearCargaEnTransitoAsync(cliente);
        var mensaje = EcoTrace.CargoTracking.Domain.OutboxMessage.CrearEntregaConfirmada(
            carga.CargaId,
            carga.VehiculoId,
            carga.ConductorId,
            carga.GeneradorTenantId,
            carga.TransportistaTenantId,
            "traza_reproceso",
            DateTime.UtcNow);
        mensaje.RegistrarFalloTransitorio("HTTP 503", DateTime.UtcNow.AddSeconds(8));
        mensaje.RegistrarFalloTransitorio("HTTP 503", DateTime.UtcNow.AddSeconds(8));
        mensaje.MarcarMuerto("HTTP 503");

        await using (var db = await fabrica.Services
            .GetRequiredService<IDbContextFactory<CargoTrackingDbContext>>()
            .CreateDbContextAsync())
        {
            db.OutboxMessages.Add(mensaje);
            await db.SaveChangesAsync();
        }

        var muertos = await cliente.ConsultarAsync("/api/outbox?estado=Muerto");
        var reprocesado = await cliente.PostearAsync($"/api/outbox/{mensaje.EventId}/reprocesar");
        var pendientes = await cliente.ConsultarAsync("/api/outbox?estado=Pendiente");
        var segundoReproceso = await cliente.PostearAsync($"/api/outbox/{mensaje.EventId}/reprocesar");
        var inexistente = await cliente.PostearAsync($"/api/outbox/{Guid.NewGuid()}/reprocesar");

        Assert.Single(muertos.Json.EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, reprocesado.Estado);
        Assert.Equal("Pendiente", reprocesado.Texto("estado"));
        Assert.Equal(0, reprocesado.Propiedad("intentos").GetInt32());
        Assert.Equal(1, pendientes.Cantidad);
        Assert.Equal(HttpStatusCode.Conflict, segundoReproceso.Estado);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.Estado);
    }

    [Fact]
    public async Task Publica_evento_con_cabeceras_y_marca_publicado_ante_un_2xx()
    {
        using var fabrica = new CargoTrackingOutboxApiFactory();
        using var cliente = fabrica.CreateClient();
        var carga = await CrearCargaEnTransitoAsync(cliente);
        var eventId = await AgregarMensajePendienteAsync(fabrica, carga);
        using var handler = new CapturingHttpMessageHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.Accepted)));
        var publicador = CrearPublicador(fabrica, handler);

        await publicador.StartAsync(CancellationToken.None);
        try
        {
            var solicitud = await handler.PrimeraSolicitud.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var mensaje = await EsperarMensajeAsync(fabrica, eventId, m => m.Estado == "Publicado");

            Assert.Equal(HttpMethod.Post, solicitud.Metodo);
            Assert.Equal("http://billing.test/api/eventos/entrega-confirmada", solicitud.Uri);
            Assert.Equal("traza_publicador", solicitud.CorrelationId);
            Assert.Equal(eventId, solicitud.IdempotencyKey);
            Assert.Equal(eventId, solicitud.Cuerpo.GetProperty("eventId").GetString());
            Assert.Equal("EntregaConfirmada", solicitud.Cuerpo.GetProperty("eventType").GetString());
            Assert.Equal(carga.CargaId, solicitud.Cuerpo.GetProperty("cargaId").GetGuid());
            Assert.Equal(carga.VehiculoId, solicitud.Cuerpo.GetProperty("vehiculoId").GetGuid());
            Assert.Equal(carga.ConductorId, solicitud.Cuerpo.GetProperty("conductorId").GetGuid());
            Assert.Equal(carga.GeneradorTenantId, solicitud.Cuerpo.GetProperty("generadorTenantId").GetGuid());
            Assert.Equal(carga.TransportistaTenantId,
                solicitud.Cuerpo.GetProperty("transportistaTenantId").GetGuid());
            Assert.Equal("traza_publicador", solicitud.Cuerpo.GetProperty("correlationId").GetString());
            Assert.Equal(0, mensaje.Intentos);
            Assert.Null(mensaje.ProximoIntentoEn);
        }
        finally
        {
            await publicador.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Un_4xx_permanente_marca_muerto_sin_reintento()
    {
        using var fabrica = new CargoTrackingOutboxApiFactory();
        using var cliente = fabrica.CreateClient();
        var carga = await CrearCargaEnTransitoAsync(cliente);
        var eventId = await AgregarMensajePendienteAsync(fabrica, carga);
        using var handler = new CapturingHttpMessageHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.BadRequest)));
        var publicador = CrearPublicador(fabrica, handler);

        await publicador.StartAsync(CancellationToken.None);
        try
        {
            await handler.PrimeraSolicitud.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var mensaje = await EsperarMensajeAsync(fabrica, eventId, m => m.Estado == "Muerto");

            Assert.Equal(0, mensaje.Intentos);
            Assert.StartsWith("ALERTA", mensaje.Error);
            Assert.Contains("HTTP 400", mensaje.Error);
            Assert.Single(handler.Solicitudes);
        }
        finally
        {
            await publicador.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(408, 2)]
    [InlineData(429, 2)]
    [InlineData(500, 2)]
    [InlineData(503, 2)]
    public async Task Fallo_transitorio_agenda_el_reintento_exponencial(int codigo, int esperaSegundos)
    {
        using var fabrica = new CargoTrackingOutboxApiFactory();
        using var cliente = fabrica.CreateClient();
        var carga = await CrearCargaEnTransitoAsync(cliente);
        var eventId = await AgregarMensajePendienteAsync(fabrica, carga);
        using var handler = new CapturingHttpMessageHandler(_ => Task.FromResult(
            new HttpResponseMessage((HttpStatusCode)codigo)));
        var publicador = CrearPublicador(fabrica, handler);
        var inicio = DateTime.UtcNow;

        await publicador.StartAsync(CancellationToken.None);
        try
        {
            await handler.PrimeraSolicitud.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var mensaje = await EsperarMensajeAsync(fabrica, eventId, m => m.Intentos == 1);

            Assert.Equal("Pendiente", mensaje.Estado);
            Assert.Contains($"HTTP {codigo}", mensaje.UltimoError);
            Assert.InRange((mensaje.ProximoIntentoEn!.Value - inicio).TotalSeconds,
                esperaSegundos - 1, esperaSegundos + 2);
            Assert.Null(mensaje.Error);
        }
        finally
        {
            await publicador.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("Timeout")]
    [InlineData("Network")]
    public async Task Timeout_y_error_de_red_se_tratan_como_fallos_transitorios(string tipoFallo)
    {
        using var fabrica = new CargoTrackingOutboxApiFactory();
        using var cliente = fabrica.CreateClient();
        var carga = await CrearCargaEnTransitoAsync(cliente);
        var eventId = await AgregarMensajePendienteAsync(fabrica, carga);
        using var handler = new CapturingHttpMessageHandler(_ =>
            tipoFallo == "Timeout"
                ? throw new TaskCanceledException("timeout", new TimeoutException())
                : throw new HttpRequestException("red no disponible"));
        var publicador = CrearPublicador(fabrica, handler);

        await publicador.StartAsync(CancellationToken.None);
        try
        {
            await handler.PrimeraSolicitud.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var mensaje = await EsperarMensajeAsync(fabrica, eventId, m => m.Intentos == 1);

            Assert.Equal("Pendiente", mensaje.Estado);
            Assert.NotNull(mensaje.UltimoError);
            Assert.NotNull(mensaje.ProximoIntentoEn);
            Assert.Null(mensaje.Error);
        }
        finally
        {
            await publicador.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 4)]
    [InlineData(2, 8)]
    [InlineData(3, 16)]
    public async Task El_backoff_duplica_la_espera_en_cada_intento(int intentosPrevios, int esperaSegundos)
    {
        using var fabrica = new CargoTrackingOutboxApiFactory();
        using var cliente = fabrica.CreateClient();
        var carga = await CrearCargaEnTransitoAsync(cliente);
        var eventId = await AgregarMensajePendienteAsync(fabrica, carga, intentosPrevios);
        using var handler = new CapturingHttpMessageHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var publicador = CrearPublicador(fabrica, handler);
        var inicio = DateTime.UtcNow;

        await publicador.StartAsync(CancellationToken.None);
        try
        {
            await handler.PrimeraSolicitud.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var mensaje = await EsperarMensajeAsync(fabrica, eventId, m => m.Intentos == intentosPrevios + 1);

            Assert.InRange((mensaje.ProximoIntentoEn!.Value - inicio).TotalSeconds,
                esperaSegundos - 1, esperaSegundos + 2);
        }
        finally
        {
            await publicador.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task El_sexto_fallo_transitorio_envia_el_mensaje_a_muerto_con_alerta()
    {
        using var fabrica = new CargoTrackingOutboxApiFactory();
        using var cliente = fabrica.CreateClient();
        var carga = await CrearCargaEnTransitoAsync(cliente);
        var eventId = await AgregarMensajePendienteAsync(fabrica, carga, 5);
        using var handler = new CapturingHttpMessageHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var publicador = CrearPublicador(fabrica, handler);

        await publicador.StartAsync(CancellationToken.None);
        try
        {
            await handler.PrimeraSolicitud.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var mensaje = await EsperarMensajeAsync(fabrica, eventId, m => m.Estado == "Muerto");

            Assert.Equal(6, mensaje.Intentos);
            Assert.StartsWith("ALERTA", mensaje.Error);
            Assert.Contains("HTTP 503", mensaje.Error);
            Assert.Single(handler.Solicitudes);
        }
        finally
        {
            await publicador.StopAsync(CancellationToken.None);
        }
    }

    private static OutboxPublisher CrearPublicador(
        CargoTrackingOutboxApiFactory fabrica, HttpMessageHandler handler)
    {
        var dbFactory = fabrica.Services.GetRequiredService<IDbContextFactory<CargoTrackingDbContext>>();
        var httpClientFactory = new TestHttpClientFactory(handler);
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:Url"] = "http://billing.test"
            })
            .Build();

        return new OutboxPublisher(dbFactory, httpClientFactory, configuracion);
    }

    private static async Task<string> AgregarMensajePendienteAsync(
        CargoTrackingOutboxApiFactory fabrica, CargaPreparada carga, int intentosPrevios = 0)
    {
        var mensaje = EcoTrace.CargoTracking.Domain.OutboxMessage.CrearEntregaConfirmada(
            carga.CargaId,
            carga.VehiculoId,
            carga.ConductorId,
            carga.GeneradorTenantId,
            carga.TransportistaTenantId,
            "traza_publicador",
            DateTime.UtcNow);
        for (var intento = 0; intento < intentosPrevios; intento++)
        {
            mensaje.RegistrarFalloTransitorio("HTTP 503", DateTime.UtcNow);
        }

        await using var db = await fabrica.Services
            .GetRequiredService<IDbContextFactory<CargoTrackingDbContext>>()
            .CreateDbContextAsync();
        db.OutboxMessages.Add(mensaje);
        await db.SaveChangesAsync();
        return mensaje.EventId;
    }

    private static async Task<EcoTrace.CargoTracking.Domain.OutboxMessage> EsperarMensajeAsync(
        CargoTrackingOutboxApiFactory fabrica,
        string eventId,
        Func<EcoTrace.CargoTracking.Domain.OutboxMessage, bool> condicion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            await using var db = await fabrica.Services
                .GetRequiredService<IDbContextFactory<CargoTrackingDbContext>>()
                .CreateDbContextAsync(timeout.Token);
            var mensaje = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(m => m.EventId == eventId, timeout.Token);
            if (condicion(mensaje))
            {
                return mensaje;
            }

            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class CapturingHttpMessageHandler(
        Func<RequestSnapshot, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        private readonly TaskCompletionSource<RequestSnapshot> _primeraSolicitud =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<RequestSnapshot> PrimeraSolicitud => _primeraSolicitud;

        public List<RequestSnapshot> Solicitudes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var snapshot = new RequestSnapshot(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.GetValues("X-Correlation-Id").Single(),
                request.Headers.GetValues("Idempotency-Key").Single(),
                JsonDocument.Parse(body).RootElement.Clone());
            Solicitudes.Add(snapshot);
            _primeraSolicitud.TrySetResult(snapshot);
            return await responder(snapshot);
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Metodo, string Uri, string CorrelationId, string IdempotencyKey, JsonElement Cuerpo);

    private static async Task<CargaPreparada> CrearCargaEnTransitoAsync(HttpClient cliente)
    {
        var generadorTenantId = Guid.NewGuid();
        var transportistaTenantId = Guid.NewGuid();
        var carga = await cliente.PostearAsync("/api/cargas", new
        {
            generadorTenantId,
            transportistaTenantId,
            descripcion = "Residuos industriales no peligrosos",
            origen = "Cali",
            destino = "Popayán",
            pesoKg = 4200
        });
        Assert.Equal(HttpStatusCode.Created, carga.Estado);
        var cargaId = carga.Id("cargaId");
        var vehiculoId = Guid.NewGuid();
        var conductorId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Created, (await cliente.PostearAsync(
            $"/api/cargas/{cargaId}/asignacion",
            new { vehiculoId, conductorId })).Estado);
        Assert.Equal(HttpStatusCode.Created, (await cliente.PostearAsync(
            $"/api/cargas/{cargaId}/seguimientos",
            new { estado = "EnTransito", ubicacion = "Ruta Panamericana", nota = (string?)null })).Estado);

        return new CargaPreparada(
            cargaId, vehiculoId, conductorId, generadorTenantId, transportistaTenantId);
    }

    private sealed record CargaPreparada(
        Guid CargaId, Guid VehiculoId, Guid ConductorId,
        Guid GeneradorTenantId, Guid TransportistaTenantId);
}

internal sealed class CargoTrackingOutboxApiFactory : WebApplicationFactory<CargoTrackingApiMarker>
{
    private readonly string _archivoDb = Path.Combine(Path.GetTempPath(), $"ecotrace-outbox-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_archivoDb}");
        builder.UseSetting("Swagger:Habilitado", "false");
        builder.ConfigureTestServices(services =>
        {
            var publisher = services.SingleOrDefault(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) &&
                descriptor.ImplementationType == typeof(OutboxPublisher));
            if (publisher is not null)
            {
                services.Remove(publisher);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        SqliteConnection.ClearAllPools();
        foreach (var sufijo in new[] { string.Empty, "-shm", "-wal" })
        {
            var ruta = _archivoDb + sufijo;
            if (File.Exists(ruta))
            {
                File.Delete(ruta);
            }
        }
    }
}