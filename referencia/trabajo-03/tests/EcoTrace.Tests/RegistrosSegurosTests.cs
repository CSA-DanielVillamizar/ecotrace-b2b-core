using System.Net;
using EcoTrace.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace EcoTrace.Tests;

/// <summary>
/// Registros seguros (ADR 0006). Cada solicitud deja un registro con el esquema mínimo <c>CorrelationId, EntityId, HttpStatus,
/// OperationName, DurationMs</c>, y en ningún registro aparece una contraseña, un JWT, un refresh token o una clave. Aquí se capturan
/// los registros de verdad que escribe el servicio (con un sumidero de Serilog) mientras se hace un inicio de sesión, un inicio de
/// sesión fallido, una llamada con token, una con un token forjado y un reenvío con <c>Idempotency-Key</c>.
/// </summary>
public sealed class RegistrosSegurosTests : IAsyncLifetime
{
    private const string Contrasena = "una-clave-larga-123";

    private readonly ApiFactory<IdentityApiMarker> _identityBase = new();
    private readonly CapturaDeRegistros _registros = new();
    private WebApplicationFactory<IdentityApiMarker> _identity = null!;

    public Task InitializeAsync()
    {
        _identity = _identityBase.WithWebHostBuilder(b =>
            b.ConfigureTestServices(servicios => servicios.AddSingleton<ILogEventSink>(_registros)));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _identity.Dispose();
        await _identityBase.DisposeAsync();
    }

    private HttpClient Operador() => _identity.CreateClient().ConToken(SeguridadDePrueba.EmitirOperador());

    /// <summary>Hace todo lo que pone secretos en el camino de una solicitud y devuelve los secretos usados.</summary>
    private async Task<(string AccessToken, string RefreshToken, string Correo)> UsarElServicioAsync()
    {
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var correo = $"persona.{sufijo}@ejemplo.co";
        var tenant = await Operador().PostearAsync("/api/tenants", new { nombre = $"Organización {sufijo}", tenantType = "Transportista" });
        var usuario = await Operador().PostearAsync("/api/users", new
        {
            tenantId = tenant.Id("tenantId"), role = "Conductor", nombre = "Persona de Prueba", email = correo, password = Contrasena
        });
        Assert.Equal(HttpStatusCode.Created, usuario.Estado);

        var login = await _identity.CreateClient().PostearAsync("/api/auth/login", new { email = correo, password = Contrasena });
        Assert.Equal(HttpStatusCode.OK, login.Estado);
        var acceso = login.Texto("accessToken");
        var refresco = login.Texto("refreshToken");

        // Una contraseña equivocada y un token forjado también dejan registro, y tampoco deben filtrar nada.
        await _identity.CreateClient().PostearAsync("/api/auth/login", new { email = correo, password = "otra-clave-equivocada-9" });
        await _identity.CreateClient().ConToken("eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJmb3JqYWRvIn0.firma-falsa").GetAsync("/api/tenants");

        // Una solicitud con token, con identificador en la ruta y con Idempotency-Key repetida.
        var cliente = _identity.CreateClient().ConToken(acceso);
        var clave = Guid.NewGuid().ToString();
        await cliente.EnviarConClaveAsync(HttpMethod.Put, $"/api/users/{usuario.Id("userId")}/perfil", new { telefono = "+57 300 123 4567" }, clave);
        await cliente.EnviarConClaveAsync(HttpMethod.Put, $"/api/users/{usuario.Id("userId")}/perfil", new { telefono = "+57 300 123 4567" }, clave);

        return (acceso, refresco, correo);
    }

    [Fact]
    public async Task Ningun_registro_lleva_contrasenas_tokens_ni_el_correo_completo()
    {
        var (acceso, refresco, correo) = await UsarElServicioAsync();

        var todo = _registros.Textos();

        Assert.NotEmpty(todo);
        foreach (var secreto in new[] { Contrasena, "otra-clave-equivocada-9", acceso, refresco, "firma-falsa", "Bearer " })
        {
            Assert.DoesNotContain(todo, texto => texto.Contains(secreto, StringComparison.Ordinal));
        }

        // El correo es un dato de contacto: no debe aparecer completo (la primera parte ya lo identifica).
        Assert.DoesNotContain(todo, texto => texto.Contains(correo, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cada_solicitud_deja_un_registro_con_el_esquema_minimo()
    {
        await UsarElServicioAsync();

        var solicitudes = _registros.Eventos.Where(e => e.Properties.ContainsKey("OperationName")).ToList();

        Assert.True(solicitudes.Count >= 6, $"Se esperaban al menos 6 solicitudes registradas y hubo {solicitudes.Count}.");
        foreach (var evento in solicitudes)
        {
            foreach (var propiedad in new[] { "OperationName", "HttpStatus", "DurationMs", "EntityId" })
            {
                Assert.True(evento.Properties.ContainsKey(propiedad), $"Falta {propiedad} en: {evento.RenderMessage()}");
            }

            Assert.True(evento.Properties.ContainsKey("CorrelationId"), $"Falta CorrelationId en: {evento.RenderMessage()}");
        }

        // La acción se nombra por su nombre, no por la dirección con datos, y el identificador de la ruta se registra aparte.
        Assert.Contains(solicitudes, e => Valor(e, "OperationName") == "Auth.Login" && Valor(e, "HttpStatus") == "200");
        Assert.Contains(solicitudes, e => Valor(e, "OperationName") == "Auth.Login" && Valor(e, "HttpStatus") == "401");
        Assert.Contains(solicitudes, e => Valor(e, "OperationName").Contains("ActualizarPerfil") && Valor(e, "EntityId") != "-");
        Assert.DoesNotContain(solicitudes, e => Valor(e, "OperationName").Contains('/'));
    }

    private static string Valor(LogEvent evento, string propiedad) =>
        evento.Properties.TryGetValue(propiedad, out var valor) ? valor.ToString().Trim('"') : string.Empty;

    /// <summary>Guarda los registros que escribe el servicio, con todas sus propiedades, para revisarlos después.</summary>
    private sealed class CapturaDeRegistros : ILogEventSink
    {
        private readonly List<LogEvent> _eventos = [];

        public IReadOnlyList<LogEvent> Eventos
        {
            get
            {
                lock (_eventos)
                {
                    return _eventos.ToList();
                }
            }
        }

        public void Emit(LogEvent evento)
        {
            lock (_eventos)
            {
                _eventos.Add(evento);
            }
        }

        /// <summary>El mensaje de cada registro y el texto de todas sus propiedades y de su excepción.</summary>
        public List<string> Textos() => Eventos
            .Select(e => string.Join(" | ", new[] { e.RenderMessage(), e.Exception?.ToString() ?? string.Empty }
                .Concat(e.Properties.Select(p => $"{p.Key}={p.Value}"))))
            .ToList();
    }
}

/// <summary>
/// Lo mismo para los tres Resource Servers: sus registros de solicitud llevan el esquema mínimo y no filtran el token, ni el válido
/// ni el forjado. Cada servicio repite el mismo <c>ApiDefaults</c>, así que se comprueba en cada uno.
/// </summary>
public sealed class RegistrosSegurosEnLosRecursosTests
{
    private static async Task ComprobarAsync<TMarker>(string rutaDeLista)
        where TMarker : class
    {
        await using var baseFabrica = new ApiFactory<TMarker>();
        var registros = new CapturaSimple();
        using var fabrica = baseFabrica.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(registros)));
        var token = new Persona(Guid.NewGuid(), Rol.Supervisor).TokenPara(ClientesDePrueba.AudienciaDe<TMarker>());
        const string forjado = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJmb3JqYWRvIn0.firma-falsa";

        Assert.Equal(HttpStatusCode.OK, (await fabrica.CreateClient().ConToken(token).GetAsync(rutaDeLista)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fabrica.CreateClient().ConToken(forjado).GetAsync(rutaDeLista)).StatusCode);

        var eventos = registros.Eventos;
        var solicitudes = eventos.Where(e => e.Properties.ContainsKey("OperationName")).ToList();
        Assert.True(solicitudes.Count >= 2, $"Se esperaban al menos 2 solicitudes registradas y hubo {solicitudes.Count} de {eventos.Count} registros: " + string.Join(" // ", eventos.Take(6).Select(e => e.RenderMessage())));
        foreach (var evento in solicitudes)
        {
            foreach (var propiedad in new[] { "OperationName", "HttpStatus", "DurationMs", "EntityId", "CorrelationId" })
            {
                Assert.True(evento.Properties.ContainsKey(propiedad), $"Falta {propiedad} en: {evento.RenderMessage()}");
            }
        }

        var todo = eventos.Select(e => e.RenderMessage() + " " + string.Join(" ", e.Properties.Select(p => $"{p.Key}={p.Value}")) + e.Exception).ToList();
        foreach (var secreto in new[] { token, forjado, "firma-falsa", "Bearer " })
        {
            Assert.DoesNotContain(todo, texto => texto.Contains(secreto, StringComparison.Ordinal));
        }
    }

    [Fact]
    public Task Fleet_registra_con_el_esquema_minimo_y_sin_el_token() => ComprobarAsync<FleetManagementApiMarker>("/api/vehiculos");

    [Fact]
    public Task Cargo_registra_con_el_esquema_minimo_y_sin_el_token() => ComprobarAsync<CargoTrackingApiMarker>("/api/cargas");

    [Fact]
    public Task Billing_registra_con_el_esquema_minimo_y_sin_el_token() => ComprobarAsync<BillingApiMarker>("/api/pagos");

    private sealed class CapturaSimple : ILogEventSink
    {
        private readonly List<LogEvent> _eventos = [];

        public IReadOnlyList<LogEvent> Eventos
        {
            get
            {
                lock (_eventos)
                {
                    return _eventos.ToList();
                }
            }
        }

        public void Emit(LogEvent evento)
        {
            lock (_eventos)
            {
                _eventos.Add(evento);
            }
        }
    }
}
