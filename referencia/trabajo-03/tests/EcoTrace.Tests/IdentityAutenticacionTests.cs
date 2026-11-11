using System.Net;
using System.Text.Json;
using EcoTrace.Identity.Api.Security;
using EcoTrace.Identity.Domain;
using EcoTrace.Identity.Infrastructure;
using EcoTrace.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Tests;

/// <summary>
/// Identity como Authorization Server (ADR 0004): inicio de sesión, refresh, cierre de sesión, la clave pública y los
/// tokens de servicio. También comprueba que sus rutas administrativas exigen el rol que corresponde.
/// </summary>
public sealed class IdentityAutenticacionTests(ApiFactory<IdentityApiMarker> fabrica) : IClassFixture<ApiFactory<IdentityApiMarker>>
{
    private static readonly JsonWebTokenHandler Manejador = new();

    private HttpClient Anonimo() => fabrica.CreateClient();

    private HttpClient Operador() => fabrica.CreateClient().ConToken(SeguridadDePrueba.EmitirOperador());

    private async Task<(Guid TenantId, string Email, string Password)> CrearUsuarioAsync(string rol, TenantType tipo = TenantType.Transportista)
    {
        var operador = Operador();
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        var tenant = await operador.PostearAsync("/api/tenants", new { nombre = $"Organización {sufijo}", tenantType = tipo.ToString() });
        var email = $"persona.{sufijo}@ejemplo.co";
        var usuario = await operador.PostearAsync("/api/users", new
        {
            tenantId = tenant.Id("tenantId"), role = rol, nombre = "Persona de Prueba", email, password = "una-clave-larga-123"
        });
        Assert.Equal(HttpStatusCode.Created, usuario.Estado);
        return (tenant.Id("tenantId"), email, "una-clave-larga-123");
    }

    private async Task<Respuesta> LoginAsync(string email, string password) =>
        await Anonimo().PostearAsync("/api/auth/login", new { email, password });

    [Fact]
    public async Task El_administrador_de_plataforma_se_siembra_desde_la_configuracion_y_puede_iniciar_sesion()
    {
        var login = await LoginAsync(SeguridadDePrueba.AdminEmail, SeguridadDePrueba.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, login.Estado);
        var token = Manejador.ReadJsonWebToken(login.Texto("accessToken"));
        Assert.Equal("Administrador", token.GetClaim("role").Value);
        Assert.Equal("true", token.GetClaim("plataforma").Value);
    }

    [Fact]
    public async Task El_token_trae_los_claims_del_ADR_se_firma_con_RS256_y_dura_quince_minutos()
    {
        var persona = await CrearUsuarioAsync("Supervisor");

        var login = await LoginAsync(persona.Email, persona.Password);

        Assert.Equal(HttpStatusCode.OK, login.Estado);
        var token = Manejador.ReadJsonWebToken(login.Texto("accessToken"));
        Assert.Equal("RS256", token.Alg);
        Assert.Equal(SeguridadDePrueba.Kid, token.Kid);
        Assert.Equal(SeguridadDePrueba.Issuer, token.Issuer);
        Assert.Equal(persona.TenantId.ToString(), token.GetClaim("tenant_id").Value);
        Assert.Equal("Supervisor", token.GetClaim("role").Value);
        Assert.False(string.IsNullOrEmpty(token.Subject));
        Assert.False(string.IsNullOrEmpty(token.Id));
        Assert.Equal(SeguridadDePrueba.TodasLasAudiencias.Order(), token.Audiences.Order());
        Assert.Equal(TimeSpan.FromMinutes(15), token.ValidTo - token.ValidFrom);
        Assert.Null(token.TryGetClaim("plataforma", out var operador) ? operador.Value : null);
    }

    [Fact]
    public async Task El_token_se_valida_solo_con_la_clave_publica_del_JWKS()
    {
        var persona = await CrearUsuarioAsync("Conductor");
        var login = await LoginAsync(persona.Email, persona.Password);
        var jwks = await Anonimo().GetStringAsync("/.well-known/jwks.json");

        var resultado = await Manejador.ValidateTokenAsync(login.Texto("accessToken"), new TokenValidationParameters
        {
            IssuerSigningKeys = new JsonWebKeySet(jwks).GetSigningKeys(),
            ValidIssuer = SeguridadDePrueba.Issuer,
            ValidAudience = SeguridadDePrueba.AudCargo,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
        });

        Assert.True(resultado.IsValid, resultado.Exception?.Message);
    }

    [Fact]
    public async Task El_JWKS_publica_la_clave_con_su_kid_y_ningun_dato_privado()
    {
        var cuerpo = await Anonimo().GetStringAsync("/.well-known/jwks.json");

        using var documento = JsonDocument.Parse(cuerpo);
        var clave = Assert.Single(documento.RootElement.GetProperty("keys").EnumerateArray());
        Assert.Equal("RSA", clave.GetProperty("kty").GetString());
        Assert.Equal("RS256", clave.GetProperty("alg").GetString());
        Assert.Equal(SeguridadDePrueba.Kid, clave.GetProperty("kid").GetString());
        foreach (var privado in new[] { "d", "p", "q", "dp", "dq", "qi" })
        {
            Assert.False(clave.TryGetProperty(privado, out _), $"El JWKS no puede traer el parámetro privado '{privado}'.");
        }
    }

    [Theory]
    [InlineData("nadie@ejemplo.co", "cualquier-clave-123")]
    [InlineData("admin@prueba.ecotrace", "clave-equivocada-123")]
    public async Task Un_inicio_de_sesion_fallido_responde_siempre_lo_mismo_sin_decir_que_campo_fallo(string email, string clave)
    {
        var respuesta = await LoginAsync(email, clave);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.Estado);
        Assert.Equal("Credenciales inválidas", respuesta.Texto("title"));
        Assert.Equal("Credenciales inválidas.", respuesta.Texto("detail"));
    }

    [Fact]
    public async Task Un_usuario_anterior_al_Trabajo_3_sin_contrasena_no_puede_iniciar_sesion()
    {
        var tenant = await Operador().PostearAsync("/api/tenants", new { nombre = "Heredada SAS", tenantType = "Generador" });
        using (var alcance = fabrica.Services.CreateScope())
        {
            var db = alcance.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var rol = await db.Roles.FirstAsync(r => r.Nombre == "Conductor");
            db.Users.Add(User.Crear(tenant.Id("tenantId"), rol.RoleId, "Cuenta Antigua", "antigua@ejemplo.co", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        var respuesta = await LoginAsync("antigua@ejemplo.co", "lo-que-sea-1234");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.Estado);
    }

    [Fact]
    public async Task La_contrasena_y_el_refresh_token_nunca_se_guardan_en_claro()
    {
        var persona = await CrearUsuarioAsync("Conductor");
        var login = await LoginAsync(persona.Email, persona.Password);
        var refresh = login.Texto("refreshToken");

        using var alcance = fabrica.Services.CreateScope();
        var db = alcance.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var usuario = await db.Users.SingleAsync(u => u.Email == persona.Email);
        Assert.StartsWith("pbkdf2-sha256$", usuario.PasswordHash);
        Assert.DoesNotContain(persona.Password, usuario.PasswordHash);

        var guardado = await db.RefreshTokens.SingleAsync(t => t.UserId == usuario.UserId);
        Assert.NotEqual(refresh, guardado.TokenHash);
        Assert.Equal(EmisorDeTokens.HashDe(refresh), guardado.TokenHash);
    }

    [Fact]
    public async Task Refresh_cambia_un_refresh_vigente_por_un_access_token_nuevo()
    {
        var persona = await CrearUsuarioAsync("Conductor");
        var login = await LoginAsync(persona.Email, persona.Password);

        var renovado = await Anonimo().PostearAsync("/api/auth/refresh", new { refreshToken = login.Texto("refreshToken") });

        Assert.Equal(HttpStatusCode.OK, renovado.Estado);
        Assert.NotEqual(login.Texto("accessToken"), renovado.Texto("accessToken"));
        Assert.Equal("Conductor", Manejador.ReadJsonWebToken(renovado.Texto("accessToken")).GetClaim("role").Value);
    }

    [Fact]
    public async Task Cerrar_sesion_revoca_el_refresh_y_es_idempotente()
    {
        var persona = await CrearUsuarioAsync("Conductor");
        var login = await LoginAsync(persona.Email, persona.Password);
        var refresh = login.Texto("refreshToken");

        var primera = await Anonimo().PostearAsync("/api/auth/logout", new { refreshToken = refresh });
        var segunda = await Anonimo().PostearAsync("/api/auth/logout", new { refreshToken = refresh });
        var desconocido = await Anonimo().PostearAsync("/api/auth/logout", new { refreshToken = "no-existe" });
        var renovar = await Anonimo().PostearAsync("/api/auth/refresh", new { refreshToken = refresh });

        Assert.Equal(HttpStatusCode.OK, primera.Estado);
        Assert.Equal(HttpStatusCode.OK, segunda.Estado);
        Assert.Equal(HttpStatusCode.OK, desconocido.Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, renovar.Estado);
    }

    [Fact]
    public async Task Un_refresh_vencido_o_inexistente_responde_401()
    {
        var persona = await CrearUsuarioAsync("Conductor");
        var login = await LoginAsync(persona.Email, persona.Password);

        using (var alcance = fabrica.Services.CreateScope())
        {
            var db = alcance.ServiceProvider.GetRequiredService<IdentityDbContext>();
            // El refresh vence a los 7 días: se retrocede su fecha de vencimiento en la base.
            await db.Database.ExecuteSqlRawAsync("UPDATE RefreshTokens SET ExpiraEn = '2000-01-01 00:00:00'");
        }

        var vencido = await Anonimo().PostearAsync("/api/auth/refresh", new { refreshToken = login.Texto("refreshToken") });
        var inexistente = await Anonimo().PostearAsync("/api/auth/refresh", new { refreshToken = "inventado" });

        Assert.Equal(HttpStatusCode.Unauthorized, vencido.Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, inexistente.Estado);
    }

    [Fact]
    public async Task Una_organizacion_suspendida_no_inicia_sesion_ni_renueva()
    {
        var persona = await CrearUsuarioAsync("Supervisor");
        var login = await LoginAsync(persona.Email, persona.Password);

        var suspension = await Operador().PostearAsync($"/api/tenants/{persona.TenantId}/suspension");
        var denuevo = await LoginAsync(persona.Email, persona.Password);
        var renovar = await Anonimo().PostearAsync("/api/auth/refresh", new { refreshToken = login.Texto("refreshToken") });

        Assert.Equal(HttpStatusCode.OK, suspension.Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, denuevo.Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, renovar.Estado);
    }

    // ---------------------------------------------------------------- token de servicio

    [Fact]
    public async Task El_token_de_servicio_trae_rol_Servicio_una_sola_audiencia_y_dura_cinco_minutos()
    {
        var respuesta = await Anonimo().PostearAsync("/api/auth/token-servicio", new
        {
            clientId = "billing", clientSecret = SeguridadDePrueba.SecretoBilling, audiencia = SeguridadDePrueba.AudFleet
        });

        Assert.Equal(HttpStatusCode.OK, respuesta.Estado);
        var token = Manejador.ReadJsonWebToken(respuesta.Texto("accessToken"));
        Assert.Equal("Servicio", token.GetClaim("role").Value);
        Assert.Equal("billing", token.Subject);
        Assert.Equal([SeguridadDePrueba.AudFleet], token.Audiences);
        Assert.Equal(TimeSpan.FromMinutes(5), token.ValidTo - token.ValidFrom);
        Assert.False(token.TryGetClaim("tenant_id", out _));
    }

    [Theory]
    [InlineData("billing", "secreto-equivocado", "ecotrace-fleet")]
    [InlineData("desconocido", "secreto-billing-de-prueba-0123", "ecotrace-fleet")]
    [InlineData("billing", "secreto-billing-de-prueba-0123", "ecotrace-cargo")]
    public async Task El_token_de_servicio_se_niega_si_el_secreto_el_cliente_o_la_audiencia_no_corresponden(
        string clientId, string clientSecret, string audiencia)
    {
        var respuesta = await Anonimo().PostearAsync("/api/auth/token-servicio", new { clientId, clientSecret, audiencia });

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.Estado);
        Assert.Equal("Credenciales inválidas", respuesta.Texto("title"));
    }

    // ---------------------------------------------------------------- Identity como Resource Server de sí mismo

    [Fact]
    public async Task Las_rutas_administrativas_sin_token_responden_401()
    {
        var cliente = Anonimo();

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostearAsync("/api/tenants", new { nombre = "X", tenantType = "Generador" })).Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.ConsultarAsync("/api/tenants")).Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.ConsultarAsync("/api/users")).Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.ConsultarAsync("/api/roles")).Estado);
    }

    [Fact]
    public async Task Solo_el_operador_crea_organizaciones_y_las_suspende()
    {
        var supervisor = await CrearUsuarioAsync("Supervisor");
        var administrador = await CrearUsuarioAsync("Administrador");
        var conductor = fabrica.CreateClient().ConToken(SeguridadDePrueba.Emitir(Rol.Conductor, Guid.NewGuid()));
        var supervisorHttp = fabrica.CreateClient().ConToken(SeguridadDePrueba.Emitir(Rol.Supervisor, supervisor.TenantId));
        // Un administrador de una organización cualquiera NO es el operador.
        var adminDeOrganizacion = fabrica.CreateClient().ConToken(SeguridadDePrueba.Emitir(Rol.Administrador, administrador.TenantId));

        var nueva = new { nombre = "Nueva SAS", tenantType = "Generador" };

        Assert.Equal(HttpStatusCode.Forbidden, (await conductor.PostearAsync("/api/tenants", nueva)).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisorHttp.PostearAsync("/api/tenants", nueva)).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminDeOrganizacion.PostearAsync("/api/tenants", nueva)).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminDeOrganizacion.PostearAsync($"/api/tenants/{supervisor.TenantId}/suspension")).Estado);
        Assert.Equal(HttpStatusCode.Created, (await Operador().PostearAsync("/api/tenants", nueva)).Estado);
    }

    [Fact]
    public async Task La_organizacion_operadora_no_se_crea_por_la_API_ni_se_suspende()
    {
        var porLaApi = await Operador().PostearAsync("/api/tenants", new { nombre = "Otra operadora", tenantType = "Plataforma" });

        using var alcance = fabrica.Services.CreateScope();
        var db = alcance.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var operadora = await db.Tenants.SingleAsync(t => t.TenantType == TenantType.Plataforma);
        var suspender = await Operador().PostearAsync($"/api/tenants/{operadora.TenantId}/suspension");

        Assert.Equal(HttpStatusCode.BadRequest, porLaApi.Estado);
        Assert.Equal(HttpStatusCode.Conflict, suspender.Estado);
    }

    [Fact]
    public async Task Un_administrador_crea_usuarios_solo_en_su_organizacion()
    {
        var admin = await CrearUsuarioAsync("Administrador");
        var otra = await CrearUsuarioAsync("Supervisor");
        var cliente = fabrica.CreateClient().ConToken(SeguridadDePrueba.Emitir(Rol.Administrador, admin.TenantId));

        var propia = await cliente.PostearAsync("/api/users", new
        {
            tenantId = admin.TenantId, role = "Conductor", nombre = "Nuevo Conductor", email = $"nuevo.{Guid.NewGuid():N}@ejemplo.co", password = "otra-clave-larga-1"
        });
        var ajena = await cliente.PostearAsync("/api/users", new
        {
            tenantId = otra.TenantId, role = "Conductor", nombre = "Intruso", email = $"intruso.{Guid.NewGuid():N}@ejemplo.co", password = "otra-clave-larga-1"
        });

        Assert.Equal(HttpStatusCode.Created, propia.Estado);
        Assert.Equal(HttpStatusCode.Forbidden, ajena.Estado);
    }

    [Theory]
    [InlineData("corta")]
    [InlineData("")]
    public async Task Una_contrasena_debil_o_ausente_se_rechaza(string password)
    {
        var admin = await CrearUsuarioAsync("Administrador");

        var respuesta = await fabrica.CreateClient().ConToken(SeguridadDePrueba.Emitir(Rol.Administrador, admin.TenantId))
            .PostearAsync("/api/users", new
            {
                tenantId = admin.TenantId, role = "Conductor", nombre = "Sin Clave", email = $"sin.clave.{Guid.NewGuid():N}@ejemplo.co", password
            });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
    }

    [Fact]
    public async Task Un_usuario_creado_por_la_API_puede_iniciar_sesion_con_su_contrasena()
    {
        var persona = await CrearUsuarioAsync("Conductor");

        var login = await LoginAsync(persona.Email, persona.Password);
        var equivocada = await LoginAsync(persona.Email, persona.Password + "x");

        Assert.Equal(HttpStatusCode.OK, login.Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, equivocada.Estado);
    }

    [Fact]
    public async Task Cada_persona_ve_su_cuenta_pero_no_la_de_otra_organizacion()
    {
        var uno = await CrearUsuarioAsync("Conductor");
        var otro = await CrearUsuarioAsync("Conductor");
        var tokenUno = (await LoginAsync(uno.Email, uno.Password)).Texto("accessToken");
        var cliente = fabrica.CreateClient().ConToken(tokenUno);
        var idUno = Guid.Parse(Manejador.ReadJsonWebToken(tokenUno).Subject);

        using var alcance = fabrica.Services.CreateScope();
        var db = alcance.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var idOtro = (await db.Users.SingleAsync(u => u.Email == otro.Email)).UserId;

        Assert.Equal(HttpStatusCode.OK, (await cliente.ConsultarAsync($"/api/users/{idUno}")).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.ConsultarAsync($"/api/users/{idOtro}")).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.ConsultarAsync("/api/users")).Estado);
    }

    [Fact]
    public async Task Las_rutas_internas_de_Identity_son_solo_para_el_rol_Servicio()
    {
        var pagoId = Guid.NewGuid();
        var admin = await CrearUsuarioAsync("Administrador");
        var usuario = fabrica.CreateClient().ConToken(SeguridadDePrueba.Emitir(Rol.Administrador, admin.TenantId));
        var servicio = fabrica.CreateClient().ConToken(SeguridadDePrueba.EmitirServicio(SeguridadDePrueba.AudIdentity));
        var cuerpo = new { pagoId, tenantId = admin.TenantId };

        Assert.Equal(HttpStatusCode.Forbidden, (await usuario.PostearAsync("/api/autorizaciones-pago", cuerpo)).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await Operador().PostearAsync("/api/autorizaciones-pago", cuerpo)).Estado);
        Assert.Equal(HttpStatusCode.Created, (await servicio.PostearAsync("/api/autorizaciones-pago", cuerpo)).Estado);
        Assert.Equal(HttpStatusCode.OK, (await servicio.ConsultarAsync($"/api/tenants/{admin.TenantId}/estado")).Estado);
    }

    [Fact]
    public async Task Fuera_de_desarrollo_sin_clave_privada_el_servicio_no_arranca()
    {
        using var sinClave = new ApiFactory<IdentityApiMarker>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Production");
            b.UseSetting("Jwt:PrivateKeyPem", string.Empty);
        });

        var ex = await Record.ExceptionAsync(() => Task.Run(() => sinClave.CreateClient()));

        Assert.NotNull(ex);
        Assert.Contains("Jwt:PrivateKeyPem", ex.ToString());
    }
}
