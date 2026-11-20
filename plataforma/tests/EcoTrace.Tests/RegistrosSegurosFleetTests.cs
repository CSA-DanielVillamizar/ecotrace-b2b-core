using System.Collections.Concurrent;
using System.Net;
using EcoTrace.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace EcoTrace.Tests;

/// <summary>
/// Registros seguros del Trabajo 3 (ADR 0006): cada solicitud a Fleet deja un registro con CorrelationId, EntityId,
/// HttpStatus, OperationName y DurationMs, y ningún registro guarda el token ni datos de personas.
/// </summary>
public sealed class RegistrosSegurosFleetTests : IDisposable
{
    private readonly FleetConIdentity _fleet = new();
    private readonly RegistrosCapturados _registros = new();
    private readonly WebApplicationFactory<FleetManagementApiMarker> _fleetConCaptura;
    private readonly HttpClient _supervisora;
    private readonly string _token;

    public RegistrosSegurosFleetTests()
    {
        // El mismo Fleet de las otras pruebas, con un destino extra que guarda en memoria todo lo que se registra.
        _fleetConCaptura = _fleet.Fleet.WithWebHostBuilder(b => b.ConfigureTestServices(servicios =>
            servicios.AddSingleton<ILogEventSink>(_registros)));
        _token = FleetConIdentity.Token(Guid.NewGuid());
        _supervisora = _fleetConCaptura.CreateClient().ConToken(_token);
    }

    public void Dispose()
    {
        _supervisora.Dispose();
        _fleet.Dispose();
    }

    [Fact]
    public async Task Cada_solicitud_deja_un_registro_con_el_esquema_minimo()
    {
        var vehiculoId = (await _supervisora.PostearAsync("/api/vehiculos", new { placa = Placa(), capacidadKg = 5000 })).Id("vehiculoId");

        var respuesta = await _supervisora.GetAsync($"/api/vehiculos/{vehiculoId}");
        var correlacion = respuesta.Headers.GetValues("X-Correlation-Id").Single();

        var registro = await EsperarAsync(e => Texto(e, "OperationName") == "Vehiculos.Obtener");
        Assert.Equal(correlacion, Texto(registro, "CorrelationId"));
        Assert.Equal(vehiculoId.ToString(), Texto(registro, "EntityId"));
        Assert.Equal("200", Texto(registro, "HttpStatus"));
        Assert.True(registro.Properties.ContainsKey("DurationMs"));
    }

    [Fact]
    public async Task Ningun_registro_guarda_el_token_ni_datos_de_personas()
    {
        await _supervisora.PostearAsync("/api/conductores", new { nombre = "Carlos Ruiz Prueba", licencia = "LIC-SECRETA-77" });
        using var conTokenAlterado = _fleetConCaptura.CreateClient().ConToken(_token[..^4] + "AAAA");
        await conTokenAlterado.ConsultarAsync("/api/vehiculos");

        await EsperarAsync(e => Texto(e, "OperationName") == "Conductores.Crear");
        await EsperarAsync(e => Texto(e, "OperationName") == "Vehiculos.Listar");

        var todo = string.Join("\n", _registros.Eventos.Select(e =>
            e.RenderMessage() + " " + string.Join(" ", e.Properties.Values)));
        var firma = _token.Split('.')[2];
        Assert.DoesNotContain(firma[..20], todo);
        Assert.DoesNotContain("Bearer", todo);
        Assert.DoesNotContain("Carlos Ruiz Prueba", todo);
        Assert.DoesNotContain("LIC-SECRETA-77", todo);
    }

    /// <summary>El registro se escribe al terminar la solicitud, así que se espera un momento a que llegue.</summary>
    private async Task<LogEvent> EsperarAsync(Func<LogEvent, bool> condicion)
    {
        for (var intento = 0; intento < 100; intento++)
        {
            var encontrado = _registros.Eventos.FirstOrDefault(condicion);
            if (encontrado is not null)
            {
                return encontrado;
            }

            await Task.Delay(50);
        }

        throw new Xunit.Sdk.XunitException("No llegó el registro esperado.");
    }

    private static string? Texto(LogEvent evento, string propiedad) =>
        evento.Properties.TryGetValue(propiedad, out var valor) ? valor.ToString().Trim('"') : null;

    private static string Placa() => "R" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private sealed class RegistrosCapturados : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Eventos { get; } = new();

        public void Emit(LogEvent logEvent) => Eventos.Enqueue(logEvent);
    }
}
