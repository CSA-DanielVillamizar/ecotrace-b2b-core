using System.Net;
using System.Security.Cryptography;
using System.Text;
using EcoTrace.Tests.TestSupport;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Tests;

/// <summary>
/// Pruebas de ataque del Trabajo 3 contra Identity como Resource Server de sí mismo. Cada token mal formado,
/// vencido o ajeno se rechaza con 401; un token válido sin permiso, con 403. El cuerpo del 401 es genérico.
/// </summary>
public sealed class AtaquesTokenIdentityTests(ApiFactory<IdentityApiMarker> fabrica)
    : IClassFixture<ApiFactory<IdentityApiMarker>>, IAsyncLifetime
{
    private const string RutaProtegida = "/api/auth/sesion";

    private HttpClient _admin = null!;
    private Guid _organizacionA;
    private Guid _organizacionB;
    private string _tokenAdminA = null!;
    private string _tokenConductorA = null!;

    public async Task InitializeAsync()
    {
        _admin = await Acceso.ClienteAdministradorAsync(fabrica.CreateClient);
        _organizacionA = (await _admin.PostearAsync("/api/tenants", new { nombre = "Transportes A", tenantType = "Transportista" })).Id("tenantId");
        _organizacionB = (await _admin.PostearAsync("/api/tenants", new { nombre = "Generadora B", tenantType = "Generador" })).Id("tenantId");

        var sufijo = Guid.NewGuid().ToString("N");
        await _admin.CrearUsuarioAsync(_organizacionA, "Administrador", $"admin.a.{sufijo}@x.co", "Admin-A-2026");
        await _admin.CrearUsuarioAsync(_organizacionA, "Conductor", $"conductor.a.{sufijo}@x.co", "Conductor-A-2026");

        _tokenAdminA = await _admin.TokenDeAsync($"admin.a.{sufijo}@x.co", "Admin-A-2026");
        _tokenConductorA = await _admin.TokenDeAsync($"conductor.a.{sufijo}@x.co", "Conductor-A-2026");
    }

    public Task DisposeAsync()
    {
        _admin.Dispose();
        return Task.CompletedTask;
    }

    // ---- 401: el token no sirve ----------------------------------------------------------------

    [Fact]
    public async Task Sin_token_responde_401()
    {
        var respuesta = await fabrica.CreateClient().ConsultarAsync(RutaProtegida);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.Estado);
    }

    [Fact]
    public async Task Un_token_vencido_responde_401()
    {
        // Vencido hace más que la tolerancia de reloj (30 s).
        var vencido = Acceso.Forjar(_organizacionA, expira: DateTime.UtcNow.AddMinutes(-2));

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoConAsync(vencido));
    }

    [Fact]
    public async Task Una_firma_alterada_en_un_byte_responde_401()
    {
        var partes = _tokenAdminA.Split('.');
        var firma = Base64UrlEncoder.DecodeBytes(partes[2]);
        firma[firma.Length / 2] ^= 0x01;
        var alterado = $"{partes[0]}.{partes[1]}.{Base64UrlEncoder.Encode(firma)}";

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoConAsync(alterado));
    }

    [Fact]
    public async Task Un_token_con_alg_none_responde_401()
    {
        var payload = _tokenAdminA.Split('.')[1];
        var encabezado = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoConAsync($"{encabezado}.{payload}."));
    }

    [Fact]
    public async Task Un_token_de_otro_emisor_responde_401()
    {
        var deProduccion = Acceso.Forjar(_organizacionA, emisor: "https://identity.ecotrace.local/produccion");

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoConAsync(deProduccion));
    }

    [Fact]
    public async Task Un_token_para_otro_modulo_responde_401()
    {
        var soloParaBilling = Acceso.Forjar(_organizacionA, audiencias: ["ecotrace-billing"]);

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoConAsync(soloParaBilling));
    }

    [Fact]
    public async Task Un_token_firmado_con_otra_clave_responde_401()
    {
        using var otraClave = RSA.Create(2048);
        var forjado = Acceso.Forjar(_organizacionA,
            credenciales: new SigningCredentials(new RsaSecurityKey(otraClave), SecurityAlgorithms.RsaSha256));

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoConAsync(forjado));
    }

    [Fact]
    public async Task Un_token_firmado_con_HS256_usando_la_clave_publica_responde_401()
    {
        // Ataque de confusión de algoritmo: firmar con HMAC usando la clave pública como secreto.
        var jwks = await fabrica.CreateClient().ConsultarAsync("/.well-known/jwks.json");
        var publica = Encoding.UTF8.GetBytes(jwks.Propiedad("keys")[0].GetRawText());
        var forjado = Acceso.Forjar(_organizacionA,
            credenciales: new SigningCredentials(new SymmetricSecurityKey(SHA256.HashData(publica)), SecurityAlgorithms.HmacSha256));

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoConAsync(forjado));
    }

    [Fact]
    public async Task El_401_es_generico_y_no_explica_el_motivo()
    {
        var vencido = await ConsultarConAsync(Acceso.Forjar(_organizacionA, expira: DateTime.UtcNow.AddMinutes(-2)), RutaProtegida);
        var otraFirma = await ConsultarConAsync(Acceso.Forjar(_organizacionA, emisor: "otro"), RutaProtegida);

        Assert.Equal(vencido.Texto("title"), otraFirma.Texto("title"));
        Assert.Equal(vencido.Texto("detail"), otraFirma.Texto("detail"));
        Assert.DoesNotContain("IDX", vencido.Json.GetRawText());
        Assert.DoesNotContain("expired", vencido.Json.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Un_token_bien_formado_de_un_emisor_valido_pasa()
    {
        Assert.Equal(HttpStatusCode.OK, await EstadoConAsync(_tokenAdminA));
        Assert.Equal(HttpStatusCode.OK, await EstadoConAsync(Acceso.Forjar(_organizacionA)));
    }

    // ---- 403: el token sirve, pero no tiene permiso ------------------------------------------

    [Fact]
    public async Task Un_token_valido_de_otra_organizacion_responde_403()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await ConsultarConAsync(_tokenAdminA, $"/api/tenants/{_organizacionB}")).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await ConsultarConAsync(_tokenAdminA, $"/api/tenants/{_organizacionB}/estado")).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await ConsultarConAsync(_tokenAdminA, $"/api/users?tenantId={_organizacionB}")).Estado);

        using var adminA = fabrica.CreateClient().ConToken(_tokenAdminA);
        var crearEnB = await adminA.PostearAsync("/api/users", new
        {
            tenantId = _organizacionB, role = "Conductor", nombre = "Intruso", email = $"intruso.{Guid.NewGuid():N}@x.co"
        });
        Assert.Equal(HttpStatusCode.Forbidden, crearEnB.Estado);
    }

    [Fact]
    public async Task La_organizacion_sale_del_token_y_cada_quien_solo_ve_la_suya()
    {
        var organizaciones = await ConsultarConAsync(_tokenAdminA, "/api/tenants");
        var usuarios = await ConsultarConAsync(_tokenAdminA, "/api/users");

        Assert.Equal(_organizacionA, Assert.Single(organizaciones.Json.EnumerateArray()).GetProperty("tenantId").GetGuid());
        Assert.All(usuarios.Json.EnumerateArray(), u => Assert.Equal(_organizacionA, u.GetProperty("tenantId").GetGuid()));
    }

    [Fact]
    public async Task Un_token_valido_con_un_rol_sin_permiso_responde_403()
    {
        using var conductor = fabrica.CreateClient().ConToken(_tokenConductorA);
        using var adminA = fabrica.CreateClient().ConToken(_tokenAdminA);

        var conductorCreaUsuario = await conductor.PostearAsync("/api/users", new
        {
            tenantId = _organizacionA, role = "Conductor", nombre = "Otro", email = $"otro.{Guid.NewGuid():N}@x.co"
        });
        var adminDeOrganizacionCreaOrganizacion = await adminA.PostearAsync("/api/tenants", new { nombre = "Nueva", tenantType = "Generador" });
        var adminDeOrganizacionSuspende = await adminA.PostearAsync($"/api/tenants/{_organizacionA}/suspension");
        var usuarioEnRutaInterna = await adminA.PostearAsync("/api/autorizaciones-pago", new { pagoId = Guid.NewGuid(), tenantId = _organizacionA });

        Assert.Equal(HttpStatusCode.Forbidden, conductorCreaUsuario.Estado);
        Assert.Equal(HttpStatusCode.Forbidden, adminDeOrganizacionCreaOrganizacion.Estado);
        Assert.Equal(HttpStatusCode.Forbidden, adminDeOrganizacionSuspende.Estado);
        Assert.Equal(HttpStatusCode.Forbidden, usuarioEnRutaInterna.Estado);
    }

    [Fact]
    public async Task Un_token_de_servicio_no_lee_usuarios_de_ninguna_organizacion()
    {
        var token = await fabrica.CreateClient().TokenServicioAsync("billing", Acceso.SecretoBilling, "ecotrace-identity");
        var adminId = (await ConsultarConAsync(_tokenAdminA, "/api/users")).Json[0].GetProperty("userId").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await ConsultarConAsync(token, "/api/users")).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await ConsultarConAsync(token, $"/api/users/{adminId}")).Estado);
    }

    [Fact]
    public async Task Un_administrador_crea_usuarios_en_su_propia_organizacion()
    {
        using var adminA = fabrica.CreateClient().ConToken(_tokenAdminA);

        var creado = await adminA.PostearAsync("/api/users", new
        {
            tenantId = _organizacionA, role = "Conductor", nombre = "Nuevo Conductor", email = $"nuevo.{Guid.NewGuid():N}@x.co"
        });

        Assert.Equal(HttpStatusCode.Created, creado.Estado);
    }

    [Fact]
    public async Task La_organizacion_operadora_no_se_puede_suspender()
    {
        var respuesta = await _admin.PostearAsync($"/api/tenants/{Acceso.OperadorTenantId}/suspension");

        Assert.Equal(HttpStatusCode.Conflict, respuesta.Estado);
    }

    private async Task<HttpStatusCode> EstadoConAsync(string token) => (await ConsultarConAsync(token, RutaProtegida)).Estado;

    private async Task<Respuesta> ConsultarConAsync(string token, string ruta)
    {
        using var cliente = fabrica.CreateClient().ConToken(token);
        return await cliente.ConsultarAsync(ruta);
    }
}
