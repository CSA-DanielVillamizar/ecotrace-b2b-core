using System.Net;
using System.Security.Cryptography;
using System.Text;
using EcoTrace.Identity.Infrastructure;
using EcoTrace.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Tests;

/// <summary>
/// Identity como Authorization Server (Trabajo 3, ADR 0004): emite, renueva y revoca tokens y publica la
/// clave pública. Son las pruebas de emisión y revocación que pide la especificación.
/// </summary>
public sealed class AutenticacionIdentityTests(ApiFactory<IdentityApiMarker> fabrica)
    : IClassFixture<ApiFactory<IdentityApiMarker>>, IAsyncLifetime
{
    private HttpClient _anonimo = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _anonimo = fabrica.CreateClient();
        _admin = await Acceso.ClienteAdministradorAsync(fabrica.CreateClient);
    }

    public Task DisposeAsync()
    {
        _anonimo.Dispose();
        _admin.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task El_login_emite_un_jwt_RS256_con_los_claims_del_contrato()
    {
        var sesion = await _anonimo.IniciarSesionAsync(Acceso.AdminEmail, Acceso.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, sesion.Estado);
        var token = new JsonWebToken(sesion.Texto("accessToken"));
        Assert.Equal("RS256", token.Alg);
        Assert.False(string.IsNullOrEmpty(token.Kid));
        Assert.Equal(Acceso.Emisor, token.Issuer);
        Assert.Equal(Acceso.OperadorTenantId.ToString(), token.GetClaim("tenant_id").Value);
        Assert.Equal("Administrador", token.GetClaim("role").Value);
        Assert.True(Guid.TryParse(token.Subject, out _));
        Assert.False(string.IsNullOrEmpty(token.Id));
        Assert.Equal(new[] { "ecotrace-identity", "ecotrace-fleet", "ecotrace-cargo", "ecotrace-billing" }, token.Audiences.ToArray());

        // 15 minutos de vida, y expiraEn coincide con exp.
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.IssuedAt);
        Assert.Equal(token.ValidTo, sesion.Propiedad("expiraEn").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));
        Assert.False(string.IsNullOrEmpty(sesion.Texto("refreshToken")));
    }

    [Fact]
    public async Task Un_login_fallido_responde_siempre_el_mismo_401_generico()
    {
        var malaContrasena = await _anonimo.IniciarSesionAsync(Acceso.AdminEmail, "no-es-la-contraseña");
        var correoInexistente = await _anonimo.IniciarSesionAsync($"nadie.{Guid.NewGuid():N}@x.co", Acceso.AdminPassword);
        var vacio = await _anonimo.PostearAsync("/api/auth/login", new { });

        foreach (var respuesta in new[] { malaContrasena, correoInexistente, vacio })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, respuesta.Estado);
            Assert.Equal("Credenciales inválidas", respuesta.Texto("title"));
            Assert.False(respuesta.Json.TryGetProperty("errors", out _));
        }
    }

    [Fact]
    public async Task El_JWKS_publica_solo_la_clave_publica_y_con_ella_se_valida_el_token_sin_llamar_a_Identity()
    {
        var jwks = await _anonimo.ConsultarAsync("/.well-known/jwks.json");
        var token = (await _anonimo.IniciarSesionAsync(Acceso.AdminEmail, Acceso.AdminPassword)).Texto("accessToken");

        Assert.Equal(HttpStatusCode.OK, jwks.Estado);
        var clave = Assert.Single(jwks.Propiedad("keys").EnumerateArray());
        Assert.Equal("RSA", clave.GetProperty("kty").GetString());
        Assert.Equal("RS256", clave.GetProperty("alg").GetString());
        Assert.Equal(new JsonWebToken(token).Kid, clave.GetProperty("kid").GetString());
        foreach (var privado in new[] { "d", "p", "q", "dp", "dq", "qi" })
        {
            Assert.False(clave.TryGetProperty(privado, out _), $"El JWKS expone el parámetro privado '{privado}'.");
        }

        // Lo que hará cada Resource Server: validar localmente con la clave del JWKS.
        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKey = new JsonWebKey(clave.GetRawText()),
            ValidIssuer = Acceso.Emisor,
            ValidAudience = "ecotrace-cargo",
            ValidAlgorithms = ["RS256"]
        });
        Assert.True(resultado.IsValid, resultado.Exception?.Message);
    }

    [Fact]
    public async Task El_refresh_cambia_un_refresh_token_vigente_por_un_access_token_nuevo()
    {
        var sesion = await _anonimo.IniciarSesionAsync(Acceso.AdminEmail, Acceso.AdminPassword);

        var renovada = await _anonimo.PostearAsync("/api/auth/refresh", new { refreshToken = sesion.Texto("refreshToken") });

        Assert.Equal(HttpStatusCode.OK, renovada.Estado);
        Assert.NotEqual(new JsonWebToken(sesion.Texto("accessToken")).Id, new JsonWebToken(renovada.Texto("accessToken")).Id);
        Assert.Equal(sesion.Texto("refreshToken"), renovada.Texto("refreshToken"));
    }

    [Fact]
    public async Task El_logout_revoca_el_refresh_token_y_es_idempotente()
    {
        var refresco = (await _anonimo.IniciarSesionAsync(Acceso.AdminEmail, Acceso.AdminPassword)).Texto("refreshToken");

        var primero = await _anonimo.PostearAsync("/api/auth/logout", new { refreshToken = refresco });
        var segundo = await _anonimo.PostearAsync("/api/auth/logout", new { refreshToken = refresco });
        var renovar = await _anonimo.PostearAsync("/api/auth/refresh", new { refreshToken = refresco });

        Assert.Equal(HttpStatusCode.OK, primero.Estado);
        Assert.Equal(HttpStatusCode.OK, segundo.Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, renovar.Estado);
    }

    [Fact]
    public async Task Un_refresh_token_inexistente_responde_401()
    {
        var respuesta = await _anonimo.PostearAsync("/api/auth/refresh", new { refreshToken = "inventado" });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.Estado);
    }

    [Fact]
    public async Task Ni_el_refresh_token_ni_la_contrasena_se_guardan_en_claro()
    {
        var refresco = (await _anonimo.IniciarSesionAsync(Acceso.AdminEmail, Acceso.AdminPassword)).Texto("refreshToken");

        using var alcance = fabrica.Services.CreateScope();
        var db = alcance.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refresco)));

        Assert.False(await db.TokensRefresco.AnyAsync(t => t.Hash == refresco));
        Assert.True(await db.TokensRefresco.AnyAsync(t => t.Hash == hash));
        var admin = await db.Users.SingleAsync(u => u.Email == Acceso.AdminEmail);
        Assert.NotNull(admin.ContrasenaHash);
        Assert.DoesNotContain(Acceso.AdminPassword, admin.ContrasenaHash);
    }

    [Fact]
    public async Task Un_usuario_de_una_organizacion_suspendida_no_inicia_sesion_ni_renueva()
    {
        var transportista = (await _admin.PostearAsync("/api/tenants", new { nombre = "Transportes Suspendibles", tenantType = "Transportista" })).Id("tenantId");
        var email = $"conductor.{Guid.NewGuid():N}@x.co";
        await _admin.CrearUsuarioAsync(transportista, "Conductor", email, "Conductor-2026");
        var refresco = (await _anonimo.IniciarSesionAsync(email, "Conductor-2026")).Texto("refreshToken");

        await _admin.PostearAsync($"/api/tenants/{transportista}/suspension");
        var login = await _anonimo.IniciarSesionAsync(email, "Conductor-2026");
        var renovar = await _anonimo.PostearAsync("/api/auth/refresh", new { refreshToken = refresco });

        Assert.Equal(HttpStatusCode.Forbidden, login.Estado);
        Assert.Equal(HttpStatusCode.Forbidden, renovar.Estado);
        Assert.Contains("suspendida", login.Texto("detail"));
    }

    [Fact]
    public async Task Un_usuario_sin_contrasena_no_puede_iniciar_sesion()
    {
        var email = $"sin.clave.{Guid.NewGuid():N}@x.co";
        await _admin.PostearAsync("/api/users", new { tenantId = Acceso.OperadorTenantId, role = "Auditor", nombre = "Sin Clave", email });

        var login = await _anonimo.IniciarSesionAsync(email, "cualquier-cosa");

        Assert.Equal(HttpStatusCode.Unauthorized, login.Estado);
    }

    [Fact]
    public async Task La_contrasena_debe_cumplir_la_politica_minima()
    {
        var respuesta = await _admin.PostearAsync("/api/users", new
        {
            tenantId = Acceso.OperadorTenantId, role = "Auditor", nombre = "Clave Corta", email = $"corta.{Guid.NewGuid():N}@x.co", password = "123"
        });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
    }

    // ---- Token de servicio ---------------------------------------------------------------------

    [Fact]
    public async Task El_token_de_servicio_dura_5_minutos_tiene_rol_Servicio_y_la_audiencia_del_destino()
    {
        var respuesta = await _anonimo.PostearAsync("/api/auth/token-servicio",
            new { clientId = "billing", clientSecret = Acceso.SecretoBilling, audiencia = "ecotrace-fleet" });

        Assert.Equal(HttpStatusCode.OK, respuesta.Estado);
        var token = new JsonWebToken(respuesta.Texto("accessToken"));
        Assert.Equal("Servicio", token.GetClaim("role").Value);
        Assert.Equal(new[] { "ecotrace-fleet" }, token.Audiences.ToArray());
        Assert.Equal("servicio:billing", token.Subject);
        Assert.False(token.TryGetClaim("tenant_id", out _));
        Assert.Equal(TimeSpan.FromMinutes(5), token.ValidTo - token.IssuedAt);
    }

    [Fact]
    public async Task El_token_de_servicio_exige_credenciales_validas_y_una_audiencia_permitida()
    {
        var malSecreto = await _anonimo.PostearAsync("/api/auth/token-servicio",
            new { clientId = "billing", clientSecret = "otro", audiencia = "ecotrace-identity" });
        var desconocido = await _anonimo.PostearAsync("/api/auth/token-servicio",
            new { clientId = "intruso", clientSecret = Acceso.SecretoBilling, audiencia = "ecotrace-identity" });
        var audienciaAjena = await _anonimo.PostearAsync("/api/auth/token-servicio",
            new { clientId = "cargo", clientSecret = Acceso.SecretoCargo, audiencia = "ecotrace-identity" });

        Assert.Equal(HttpStatusCode.Unauthorized, malSecreto.Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, desconocido.Estado);
        Assert.Equal(HttpStatusCode.Forbidden, audienciaAjena.Estado);
    }

    [Fact]
    public async Task Las_rutas_internas_aceptan_el_token_de_servicio_y_rechazan_el_de_un_usuario()
    {
        var transportista = (await _admin.PostearAsync("/api/tenants", new { nombre = "Transportes Internos", tenantType = "Transportista" })).Id("tenantId");
        using var billing = fabrica.CreateClient().ConToken(
            await _anonimo.TokenServicioAsync("billing", Acceso.SecretoBilling, "ecotrace-identity"));

        var comoServicio = await billing.PostearAsync("/api/autorizaciones-pago", new { pagoId = Guid.NewGuid(), tenantId = transportista });
        var comoAdmin = await _admin.PostearAsync("/api/autorizaciones-pago", new { pagoId = Guid.NewGuid(), tenantId = transportista });

        Assert.Equal(HttpStatusCode.Created, comoServicio.Estado);
        Assert.Equal(HttpStatusCode.Forbidden, comoAdmin.Estado);
    }

    [Fact]
    public async Task La_sesion_actual_muestra_lo_que_Identity_leyo_del_token()
    {
        var sesion = await _admin.ConsultarAsync("/api/auth/sesion");

        Assert.Equal(HttpStatusCode.OK, sesion.Estado);
        Assert.Equal("Administrador", sesion.Texto("role"));
        Assert.Equal(Acceso.OperadorTenantId, sesion.Id("tenantId"));
    }
}
