using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using EcoTrace.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace EcoTrace.Tests;

/// <summary>
/// Despliegue de Identity (Trabajo 3, ADR 0006): el entorno real que responde, el dato conocido de producción para la
/// Capa 2 y los registros con el esquema mínimo, sin contraseñas, tokens, secretos ni datos de contacto.
/// </summary>
public sealed class DespliegueIdentityTests : IDisposable
{
    private const string TenantVerificacion = "0e7ace00-0000-4000-8000-0000000000a1";
    private const string AdminProduccion = "admin@ecotrace.co";
    private const string PasswordProduccion = "Prod-Admin-De-Prueba-2026";

    private readonly ApiFactory<IdentityApiMarker> _base = new();

    public void Dispose() => _base.Dispose();

    [Fact]
    public async Task En_desarrollo_el_servicio_dice_que_es_de_desarrollo()
    {
        var meta = await _base.CreateClient().ConsultarAsync("/api/_meta/entorno");

        Assert.Equal(HttpStatusCode.OK, meta.Estado);
        Assert.Equal("identity", meta.Texto("servicio"));
        Assert.Equal("desarrollo", meta.Texto("entorno"));
        Assert.Equal("0.3.0", meta.Texto("version"));
    }

    [Fact]
    public async Task En_produccion_dice_produccion_emite_con_su_emisor_y_tiene_el_dato_conocido()
    {
        using var produccion = Produccion();
        var cliente = produccion.CreateClient();

        var meta = await cliente.ConsultarAsync("/api/_meta/entorno");
        var token = await cliente.TokenDeAsync(AdminProduccion, PasswordProduccion);
        var verificacion = await produccion.CreateClient().ConToken(token).ConsultarAsync($"/api/tenants/{TenantVerificacion}");

        Assert.Equal("produccion", meta.Texto("entorno"));
        Assert.Equal("https://identity.ecotrace.local/produccion", Acceso.Payload(token).GetProperty("iss").GetString());
        Assert.Equal(HttpStatusCode.OK, verificacion.Estado);
        Assert.Equal("Verificación de producción", verificacion.Texto("nombre"));
    }

    [Fact]
    public async Task El_dato_conocido_de_produccion_no_existe_en_desarrollo()
    {
        var admin = await Acceso.ClienteAdministradorAsync(_base.CreateClient);

        var verificacion = await admin.ConsultarAsync($"/api/tenants/{TenantVerificacion}");

        Assert.Equal(HttpStatusCode.NotFound, verificacion.Estado);
    }

    [Fact]
    public async Task Un_token_de_desarrollo_no_sirve_en_produccion()
    {
        using var produccion = Produccion();
        var deDesarrollo = await _base.CreateClient().TokenAdministradorAsync();

        var respuesta = await produccion.CreateClient().ConToken(deDesarrollo).ConsultarAsync("/api/auth/sesion");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.Estado);
    }

    [Fact]
    public async Task Cada_solicitud_deja_un_registro_con_el_esquema_minimo_y_sin_datos_sensibles()
    {
        var sumidero = new SumideroDePrueba();
        using var fabrica = _base.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ILogEventSink>(sumidero)));
        var anonimo = fabrica.CreateClient();

        var sesion = await anonimo.IniciarSesionAsync(Acceso.AdminEmail, Acceso.AdminPassword);
        var accessToken = sesion.Texto("accessToken");
        var refreshToken = sesion.Texto("refreshToken");
        await anonimo.IniciarSesionAsync(Acceso.AdminEmail, "Contraseña-Equivocada-77");
        await anonimo.PostearAsync("/api/auth/refresh", new { refreshToken });
        var servicio = await anonimo.PostearAsync("/api/auth/token-servicio",
            new { clientId = "billing", clientSecret = Acceso.SecretoBilling, audiencia = "ecotrace-identity" });
        var forjado = Acceso.Forjar(Acceso.OperadorTenantId, emisor: "https://otro.emisor");
        await fabrica.CreateClient().ConToken(forjado).ConsultarAsync("/api/auth/sesion");
        using (var conToken = fabrica.CreateClient().ConToken(accessToken))
        using (var cambio = new HttpRequestMessage(HttpMethod.Put, "/api/perfil") { Content = JsonContent.Create(new { nombre = "Administrador", telefono = "+57 315 999 8877" }) })
        {
            cambio.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            await conToken.SendAsync(cambio);
        }

        var solicitudes = sumidero.Eventos.Where(e => e.MessageTemplate.Text.StartsWith("{OperationName}", StringComparison.Ordinal)).ToList();

        // El esquema mínimo, en cada solicitud.
        Assert.True(solicitudes.Count >= 6, $"Solo hubo {solicitudes.Count} registros de solicitud.");
        Assert.All(solicitudes, e =>
        {
            foreach (var campo in new[] { "CorrelationId", "EntityId", "HttpStatus", "OperationName", "DurationMs" })
            {
                Assert.True(e.Properties.ContainsKey(campo), $"Falta {campo} en: {e.RenderMessage()}");
            }
        });

        var login = solicitudes.First(e => Texto(e, "OperationName") == "Auth.Login" && Texto(e, "HttpStatus") == "200");
        Assert.True(Guid.TryParse(Texto(login, "EntityId"), out _), "La entidad del login debe ser el UserId.");
        Assert.Contains(solicitudes, e => Texto(e, "OperationName") == "Auth.Login" && Texto(e, "HttpStatus") == "401");
        Assert.Contains(solicitudes, e => Texto(e, "OperationName") == "Perfil.Actualizar" && Texto(e, "HttpStatus") == "200");

        // Nada sensible en ningún registro: ni en el mensaje ni en sus propiedades.
        var todo = string.Join("\n", sumidero.Eventos.Select(e =>
            e.RenderMessage() + " " + string.Join(" ", e.Properties.Select(p => p.Value.ToString())) + " " + e.Exception));
        foreach (var sensible in new[]
                 {
                     Acceso.AdminPassword, "Contraseña-Equivocada-77", accessToken, refreshToken, servicio.Texto("accessToken"),
                     forjado, Acceso.SecretoBilling, Acceso.AdminEmail, "315 999 8877", "+573159998877"
                 })
        {
            Assert.DoesNotContain(sensible, todo);
        }

        // Tampoco un pedazo reconocible del JWT (la firma) ni del refresh token.
        Assert.DoesNotContain(accessToken.Split('.')[2], todo);
        Assert.DoesNotContain(refreshToken[..20], todo);
    }

    private WebApplicationFactory<IdentityApiMarker> Produccion() =>
        _base.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Identity:Operador:AdminPassword", PasswordProduccion);
        });

    private static string Texto(LogEvent evento, string propiedad) =>
        evento.Properties.TryGetValue(propiedad, out var valor) ? valor.ToString().Trim('"') : string.Empty;

    private sealed class SumideroDePrueba : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _eventos = new();

        public IReadOnlyCollection<LogEvent> Eventos => _eventos;

        public void Emit(LogEvent logEvent) => _eventos.Enqueue(logEvent);
    }
}
