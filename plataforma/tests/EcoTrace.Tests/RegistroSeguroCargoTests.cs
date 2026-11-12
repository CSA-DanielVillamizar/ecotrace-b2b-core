using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using EcoTrace.CargoTracking.Api.Extensions;
using EcoTrace.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EcoTrace.Tests;

/// <summary>
/// Registros seguros (ADR 0006). De cada solicitud, Cargo &amp; Tracking anota solo cinco datos: la correlación, el nombre de la
/// operación, el código con el que respondió, cuánto tardó y el identificador de la entidad. Nunca el token, la contraseña, el
/// cuerpo ni la clave de idempotencia. Estas pruebas cambian el destino del registro por uno que guarda las entradas, para
/// poder revisar exactamente qué se anota.
/// </summary>
public sealed class RegistroSeguroCargoTests : IAsyncLifetime
{
    /// <summary>Un registro de mentira que guarda cada entrada en una lista, para que la prueba la revise.</summary>
    private sealed class Capturador : IRegistroDeSolicitudes
    {
        /// <summary>Las entradas anotadas hasta ahora.</summary>
        private readonly ConcurrentQueue<EntradaDeRegistro> _entradas = new();

        /// <summary>Las entradas anotadas hasta ahora.</summary>
        public IReadOnlyCollection<EntradaDeRegistro> Entradas => _entradas;

        /// <summary>Guarda la entrada en la lista.</summary>
        public void Registrar(EntradaDeRegistro entrada) => _entradas.Enqueue(entrada);
    }

    /// <summary>El servicio de Cargo levantado en memoria.</summary>
    private readonly ApiFactory<CargoTrackingApiMarker> _fabrica = new();
    /// <summary>El registro de mentira que captura lo que se anota.</summary>
    private readonly Capturador _registro = new();
    /// <summary>El mismo servicio, pero con el registro reemplazado por el de mentira.</summary>
    private readonly WebApplicationFactory<CargoTrackingApiMarker> _conCaptura;
    /// <summary>Un cliente que llama sin token.</summary>
    private readonly HttpClient _sinToken;
    /// <summary>La organización que pide las cargas.</summary>
    private readonly Guid _generador = Guid.NewGuid();
    /// <summary>La organización que las transporta.</summary>
    private readonly Guid _transportista = Guid.NewGuid();

    /// <summary>Prepara el servicio con el registro de mentira y un cliente sin token.</summary>
    public RegistroSeguroCargoTests()
    {
        // El mismo servidor de pruebas, pero con el registro reemplazado por uno que guarda las entradas.
        _conCaptura = _fabrica.WithWebHostBuilder(b =>
            b.ConfigureTestServices(servicios => servicios.AddSingleton<IRegistroDeSolicitudes>(_registro)));
        _sinToken = _conCaptura.CreateClient();
    }

    /// <summary>No hay nada que preparar antes de cada prueba.</summary>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Apaga el servicio al terminar la prueba.</summary>
    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    /// <summary>Un cliente que llama con un token válido de ese rol y de esa organización.</summary>
    private HttpClient Como(string rol, Guid organizacion) =>
        _conCaptura.CreateClient().ConToken(SeguridadDePrueba.Emitir(rol, organizacion));

    /// <summary>Los datos para crear una carga de ejemplo.</summary>
    private object NuevaCarga() => new
    {
        generadorTenantId = _generador,
        transportistaTenantId = _transportista,
        descripcion = "Residuos industriales no peligrosos",
        origen = "Cali",
        destino = "Popayán",
        pesoKg = 4200
    };

    /// <summary>El servidor de pruebas puede entregar la respuesta un instante antes de que se anote el registro, así que se espera un momento a que aparezca en vez de leerlo de golpe.</summary>
    private async Task<EntradaDeRegistro> EsperarAsync(Func<EntradaDeRegistro, bool> condicion)
    {
        for (var intento = 0; intento < 100; intento++)
        {
            if (_registro.Entradas.FirstOrDefault(condicion) is { } encontrada)
            {
                return encontrada;
            }

            await Task.Delay(20);
        }

        throw new InvalidOperationException("No apareció el registro esperado.");
    }

    /// <summary>Cada solicitud deja un registro con los cinco datos del esquema: la correlación coincide con la de la respuesta y la entidad es la carga consultada.</summary>
    [Fact]
    public async Task Cada_solicitud_deja_un_registro_con_el_esquema_minimo()
    {
        var respuesta = await Como(Rol.Supervisor, _generador).PostAsJsonAsync("/api/cargas", NuevaCarga());
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var cargaId = (await Respuesta.De(respuesta)).Id("cargaId");

        var creada = await EsperarAsync(e => e.OperationName == "Cargas.Crear");
        Assert.Equal(201, creada.HttpStatus);
        Assert.Equal(respuesta.Headers.GetValues("X-Correlation-Id").Single(), creada.CorrelationId);
        Assert.True(creada.DurationMs >= 0);

        await Como(Rol.Auditor, _generador).GetAsync($"/api/cargas/{cargaId}");
        var consultada = await EsperarAsync(e => e.OperationName == "Cargas.Obtener");
        Assert.Equal(200, consultada.HttpStatus);
        Assert.Equal(cargaId.ToString(), consultada.EntityId);
    }

    /// <summary>Los rechazos (401, 409, 403) quedan anotados con su código final y el nombre correcto de la operación, incluso cuando otro paso convierte el error en respuesta.</summary>
    [Fact]
    public async Task Los_rechazos_se_registran_con_su_codigo_final()
    {
        // Sin token: 401.
        await _sinToken.GetAsync("/api/cargas");
        Assert.Equal(401, (await EsperarAsync(e => e.OperationName == "Cargas.Listar")).HttpStatus);

        // Una carga asignada dos veces: el dominio lanza un conflicto y otro paso lo convierte en un 409. El registro
        // debe decir 409 y no el código provisional, por eso va antes que ese paso.
        var creada = await Como(Rol.Supervisor, _generador).PostAsJsonAsync("/api/cargas", NuevaCarga());
        var cargaId = (await Respuesta.De(creada)).Id("cargaId");
        var asignacion = new { vehiculoId = Guid.NewGuid(), conductorId = Guid.NewGuid() };
        await Como(Rol.Supervisor, _transportista).PostAsJsonAsync($"/api/cargas/{cargaId}/asignacion", asignacion);
        await Como(Rol.Supervisor, _transportista).PostAsJsonAsync($"/api/cargas/{cargaId}/asignacion", asignacion);
        Assert.Equal(409, (await EsperarAsync(e => e.OperationName == "Cargas.Asignar" && e.HttpStatus == 409)).HttpStatus);

        // Una organización ajena: 403.
        await Como(Rol.Supervisor, Guid.NewGuid()).GetAsync($"/api/cargas/{cargaId}");
        Assert.Equal(403, (await EsperarAsync(e => e.OperationName == "Cargas.Obtener" && e.HttpStatus == 403)).HttpStatus);
    }

    /// <summary>Después de una entrega con token, número de operación y una nota privada, ninguno de esos tres datos aparece en lo que se anota.</summary>
    [Fact]
    public async Task Ni_el_token_ni_la_clave_ni_la_nota_llegan_al_registro()
    {
        var token = SeguridadDePrueba.Emitir(Rol.Conductor, _transportista);
        var creada = await Como(Rol.Supervisor, _generador).PostAsJsonAsync("/api/cargas", NuevaCarga());
        var cargaId = (await Respuesta.De(creada)).Id("cargaId");
        await Como(Rol.Supervisor, _transportista).PostAsJsonAsync(
            $"/api/cargas/{cargaId}/asignacion", new { vehiculoId = Guid.NewGuid(), conductorId = Guid.NewGuid() });
        await Como(Rol.Conductor, _transportista).PostAsJsonAsync(
            $"/api/cargas/{cargaId}/seguimientos", new { estado = "EnTransito", ubicacion = "Santander de Quilichao" });

        var clave = Guid.NewGuid().ToString();
        const string nota = "NOTA-PRIVADA-DEL-CONDUCTOR";
        using var entrega = new HttpRequestMessage(HttpMethod.Post, $"/api/cargas/{cargaId}/seguimientos")
        {
            Content = JsonContent.Create(new { estado = "Entregado", ubicacion = "Popayán", nota })
        };
        entrega.Headers.Add("Idempotency-Key", clave);
        entrega.Headers.Authorization = new("Bearer", token);
        var respuesta = await _conCaptura.CreateClient().SendAsync(entrega);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        var entrada = await EsperarAsync(e => e.OperationName == "Cargas.RegistrarSeguimiento" && e.HttpStatus == 201 && e.EntityId == cargaId.ToString());
        var todo = string.Join(" | ", _registro.Entradas.Select(e => e.ToString()));

        Assert.Equal(cargaId.ToString(), entrada.EntityId);
        Assert.DoesNotContain(token, todo);
        Assert.DoesNotContain("Bearer", todo);
        Assert.DoesNotContain(clave, todo);
        Assert.DoesNotContain(nota, todo);
    }
}
