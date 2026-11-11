using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EcoTrace.Billing.Api;
using EcoTrace.Billing.Domain;
using EcoTrace.Tests.TestSupport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Tests;

/// <summary>
/// Pruebas de ataque y validación del Resource Server de Billing & Escrow (Trabajo 3, ADR 0004 y ADR 0005).
/// Verifica el rechazo limpio con 401 (sin token, vencido, firma forjada, alg: none, iss y aud inválidos)
/// y con 403 (organización ajena, rol sin privilegios), además de la idempotencia y el conflicto B.
/// </summary>
public sealed class AtaquesTokenBillingTests : IClassFixture<ApiFactory<BillingApiMarker>>, IAsyncLifetime
{
    private readonly HttpClient _cliente;
    private readonly Guid _organizacionA = Guid.NewGuid();
    private readonly Guid _organizacionB = Guid.NewGuid();
    private readonly Guid _transportistaA = Guid.NewGuid();
    private readonly Guid _cargaId = Guid.NewGuid();

    private string _tokenAdminA = null!;
    private string _tokenConductorA = null!;
    private string _tokenAdminB = null!;
    private Guid _pagoId;

    public AtaquesTokenBillingTests(ApiFactory<BillingApiMarker> fabrica)
    {
        _cliente = fabrica.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(servicios =>
            {
                servicios.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    o.ConfigurationManager = null;
                    o.TokenValidationParameters.IssuerSigningKey = Acceso.CredencialesDeIdentity.Key;
                });
            });
        }).CreateClient();
    }

    public async Task InitializeAsync()
    {
        _tokenAdminA = Acceso.Forjar(_organizacionA, rol: "Administrador", audiencias: ["ecotrace-billing"]);
        _tokenConductorA = Acceso.Forjar(_organizacionA, rol: "Conductor", audiencias: ["ecotrace-billing"]);
        _tokenAdminB = Acceso.Forjar(_organizacionB, rol: "Administrador", audiencias: ["ecotrace-billing"]);

        // Creamos un pago base para la organización A usando el token de Administrador de la organización A
        using var solicitud = CrearSolicitud(HttpMethod.Post, "/api/pagos", _tokenAdminA, new
        {
            generadorTenantId = _organizacionA,
            transportistaTenantId = _transportistaA,
            cargaId = _cargaId,
            monto = 500_000m
        });

        var respuesta = await _cliente.SendAsync(solicitud);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        var json = await respuesta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        _pagoId = json.GetProperty("pago").GetProperty("pagoId").GetGuid();
    }

    public Task DisposeAsync()
    {
        _cliente.Dispose();
        return Task.CompletedTask;
    }

    private static HttpRequestMessage CrearSolicitud(HttpMethod metodo, string ruta, string? token = null, object? cuerpo = null)
    {
        var solicitud = new HttpRequestMessage(metodo, ruta);
        if (!string.IsNullOrEmpty(token))
        {
            solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (cuerpo is not null)
        {
            solicitud.Content = JsonContent.Create(cuerpo);
        }
        return solicitud;
    }

    // ---- 401: el token no sirve (ADR 0004) ----------------------------------------------------

    [Fact]
    public async Task Sin_token_responde_401()
    {
        using var solicitud = CrearSolicitud(HttpMethod.Get, $"/api/pagos/{_pagoId}");
        var respuesta = await _cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.True(respuesta.Headers.Contains("WWW-Authenticate"));
    }

    [Fact]
    public async Task Token_vencido_responde_401()
    {
        var vencido = Acceso.Forjar(_organizacionA, audiencias: ["ecotrace-billing"], expira: DateTime.UtcNow.AddMinutes(-5));
        using var solicitud = CrearSolicitud(HttpMethod.Get, $"/api/pagos/{_pagoId}", vencido);
        var respuesta = await _cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Firma_alterada_responde_401()
    {
        var partes = _tokenAdminA.Split('.');
        var firma = Base64UrlEncoder.DecodeBytes(partes[2]);
        firma[firma.Length / 2] ^= 0x01;
        var alterado = $"{partes[0]}.{partes[1]}.{Base64UrlEncoder.Encode(firma)}";

        using var solicitud = CrearSolicitud(HttpMethod.Get, $"/api/pagos/{_pagoId}", alterado);
        var respuesta = await _cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Algoritmo_none_responde_401()
    {
        var payload = _tokenAdminA.Split('.')[1];
        var encabezado = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");
        var tokenNone = $"{encabezado}.{payload}.";

        using var solicitud = CrearSolicitud(HttpMethod.Get, $"/api/pagos/{_pagoId}", tokenNone);
        var respuesta = await _cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Emisor_de_otro_entorno_responde_401()
    {
        var deProduccion = Acceso.Forjar(_organizacionA, emisor: "https://identity.ecotrace.local/produccion", audiencias: ["ecotrace-billing"]);
        using var solicitud = CrearSolicitud(HttpMethod.Get, $"/api/pagos/{_pagoId}", deProduccion);
        var respuesta = await _cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Audiencia_de_otro_modulo_responde_401()
    {
        var paraFleet = Acceso.Forjar(_organizacionA, audiencias: ["ecotrace-fleet"]);
        using var solicitud = CrearSolicitud(HttpMethod.Get, $"/api/pagos/{_pagoId}", paraFleet);
        var respuesta = await _cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    // ---- 403: token válido pero sin permisos (ADR 0004) ---------------------------------------

    [Fact]
    public async Task Token_de_otra_organizacion_responde_403()
    {
        // Un administrador de la organización B intenta consultar el pago de la organización A
        using var solicitud = CrearSolicitud(HttpMethod.Get, $"/api/pagos/{_pagoId}", _tokenAdminB);
        var respuesta = await _cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task Token_con_rol_sin_permiso_responde_403()
    {
        // Un Conductor de la organización A intenta liberar fondos (operación reservada a Administrador)
        using var solicitud = CrearSolicitud(HttpMethod.Post, $"/api/pagos/{_pagoId}/liberar", _tokenConductorA);
        var respuesta = await _cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    // ---- Control positivo: token válido con permisos (200 OK) --------------------------------

    [Fact]
    public async Task Token_valido_con_permiso_responde_exito()
    {
        using var solicitud = CrearSolicitud(HttpMethod.Get, $"/api/pagos/{_pagoId}", _tokenAdminA);
        var respuesta = await _cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    // ---- Móvil: Idempotencia y Conflicto Tipo B (ADR 0005) -----------------------------------

    [Fact]
    public async Task Reintento_con_mismo_OperationId_no_duplica_evidencia()
    {
        var operationId = Guid.NewGuid().ToString("D");
        using var solicitud1 = CrearSolicitud(HttpMethod.Post, $"/api/pagos/{_pagoId}/evidencias", _tokenAdminA, new { evidencia = "Foto remisión #101" });
        solicitud1.Headers.Add("Idempotency-Key", operationId);

        var respuesta1 = await _cliente.SendAsync(solicitud1);
        Assert.Equal(HttpStatusCode.OK, respuesta1.StatusCode);

        // Reintento de sincronización con la misma clave de idempotencia
        using var solicitud2 = CrearSolicitud(HttpMethod.Post, $"/api/pagos/{_pagoId}/evidencias", _tokenAdminA, new { evidencia = "Foto remisión #101" });
        solicitud2.Headers.Add("Idempotency-Key", operationId);

        var respuesta2 = await _cliente.SendAsync(solicitud2);
        Assert.Equal(HttpStatusCode.OK, respuesta2.StatusCode);

        // Verificamos que en la auditoría del pago no se haya duplicado el evento
        using var solicitudAuditoria = CrearSolicitud(HttpMethod.Get, $"/api/pagos/{_pagoId}/auditoria", _tokenAdminA);
        var respuestaAuditoria = await _cliente.SendAsync(solicitudAuditoria);
        var auditoria = await respuestaAuditoria.Content.ReadFromJsonAsync<System.Text.Json.JsonElement[]>();

        Assert.NotNull(auditoria);
        var eventosEvidencia = auditoria.Count(a => a.GetProperty("accion").GetString()!.Contains("Foto remisión #101"));
        Assert.Equal(1, eventosEvidencia);
    }

    [Fact]
    public async Task Registrar_evidencia_en_pago_ya_liberado_responde_conflicto_tipo_B()
    {
        // Creamos un nuevo pago y lo liberamos
        var cargaId = Guid.NewGuid();
        using var solCrear = CrearSolicitud(HttpMethod.Post, "/api/pagos", _tokenAdminA, new
        {
            generadorTenantId = _organizacionA,
            transportistaTenantId = _transportistaA,
            cargaId,
            monto = 200_000m
        });

        var resCrear = await _cliente.SendAsync(solCrear);
        Assert.Equal(HttpStatusCode.Created, resCrear.StatusCode);
        var jsonCrear = await resCrear.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var pagoId = jsonCrear.GetProperty("pago").GetProperty("pagoId").GetGuid();

        // Liberamos el pago
        using var solLiberar = CrearSolicitud(HttpMethod.Post, $"/api/pagos/{pagoId}/liberar", _tokenAdminA);
        var resLiberar = await _cliente.SendAsync(solLiberar);
        Assert.Equal(HttpStatusCode.OK, resLiberar.StatusCode);

        // Intentamos registrar evidencia tardía desde la app sobre el pago ya liberado
        using var solEvidencia = CrearSolicitud(HttpMethod.Post, $"/api/pagos/{pagoId}/evidencias", _tokenAdminA, new { evidencia = "Remisión tardía #999" });
        solEvidencia.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("D"));

        var respuesta = await _cliente.SendAsync(solEvidencia);
        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);

        var conflicto = await respuesta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.True(conflicto.TryGetProperty("tipoConflicto", out var tipoConflicto));
        Assert.Equal("B", tipoConflicto.GetString());
    }
}
