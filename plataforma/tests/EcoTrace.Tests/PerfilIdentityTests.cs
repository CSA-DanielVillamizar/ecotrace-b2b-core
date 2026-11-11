using System.Net;
using System.Net.Http.Json;
using EcoTrace.Tests.TestSupport;
using Microsoft.IdentityModel.JsonWebTokens;

namespace EcoTrace.Tests;

/// <summary>
/// El perfil del usuario como lo usa la app (Trabajo 3, ADR 0005): leerlo, cambiarlo y cerrar las otras sesiones,
/// con idempotencia por <c>Idempotency-Key</c> y rechazos con <c>tipoConflicto</c>.
/// </summary>
public sealed class PerfilIdentityTests(ApiFactory<IdentityApiMarker> fabrica)
    : IClassFixture<ApiFactory<IdentityApiMarker>>, IAsyncLifetime
{
    private HttpClient _admin = null!;
    private Guid _organizacion;

    public async Task InitializeAsync()
    {
        _admin = await Acceso.ClienteAdministradorAsync(fabrica.CreateClient);
        _organizacion = (await _admin.PostearAsync("/api/tenants", new { nombre = "Transportes Perfil", tenantType = "Transportista" })).Id("tenantId");
    }

    public Task DisposeAsync()
    {
        _admin.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task El_perfil_sale_del_token_con_la_organizacion_y_su_estado()
    {
        var (cliente, email) = await ConductorAsync();

        var perfil = await cliente.ConsultarAsync("/api/perfil");

        Assert.Equal(HttpStatusCode.OK, perfil.Estado);
        Assert.Equal(email, perfil.Texto("email"));
        Assert.Equal("Conductor", perfil.Texto("role"));
        Assert.Equal("Transportes Perfil", perfil.Texto("organizacion"));
        Assert.Equal("Activo", perfil.Texto("estadoOrganizacion"));
    }

    [Fact]
    public async Task Cambiar_el_perfil_normaliza_el_telefono_y_lo_devuelve()
    {
        var (cliente, _) = await ConductorAsync();

        var cambio = await PonerAsync(cliente, new { nombre = "Carlos Ruiz Díaz", telefono = "+57 300-123 4567" }, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, cambio.Estado);
        Assert.Equal("Carlos Ruiz Díaz", cambio.Texto("nombre"));
        Assert.Equal("+573001234567", cambio.Texto("telefono"));
        Assert.Equal("+573001234567", (await cliente.ConsultarAsync("/api/perfil")).Texto("telefono"));
    }

    [Fact]
    public async Task Reenviar_una_clave_ya_procesada_devuelve_el_resultado_anterior_sin_pisar_un_cambio_mas_nuevo()
    {
        var (cliente, _) = await ConductorAsync();
        var primera = Guid.NewGuid();

        await PonerAsync(cliente, new { nombre = "Carlos Ruiz", telefono = "3001111111" }, primera);
        await PonerAsync(cliente, new { nombre = "Carlos Ruiz", telefono = "3002222222" }, Guid.NewGuid());

        // La app no recibió la respuesta de la primera y la reenvía, con la misma clave.
        var reenvio = await PonerAsync(cliente, new { nombre = "Carlos Ruiz", telefono = "3001111111" }, primera);

        Assert.Equal(HttpStatusCode.OK, reenvio.Estado);
        Assert.Equal("3001111111", reenvio.Texto("telefono"));
        Assert.Equal("3002222222", (await cliente.ConsultarAsync("/api/perfil")).Texto("telefono"));
    }

    [Fact]
    public async Task La_misma_clave_enviada_a_la_vez_aplica_el_cambio_una_sola_vez()
    {
        var (cliente, _) = await ConductorAsync();
        var clave = Guid.NewGuid();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => PonerAsync(cliente, new { nombre = "Carlos Ruiz", telefono = "3003333333" }, clave)));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.Estado));
        Assert.All(respuestas, r => Assert.Equal("3003333333", r.Texto("telefono")));
    }

    [Fact]
    public async Task Un_telefono_invalido_es_conflicto_A_y_no_se_guarda_la_clave()
    {
        var (cliente, _) = await ConductorAsync();
        var clave = Guid.NewGuid();

        var invalido = await PonerAsync(cliente, new { nombre = "Carlos Ruiz", telefono = "12ab" }, clave);
        var corregido = await PonerAsync(cliente, new { nombre = "Carlos Ruiz", telefono = "3004444444" }, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, invalido.Estado);
        Assert.Equal("A", invalido.Texto("tipoConflicto"));
        Assert.Contains("teléfono", invalido.Texto("detail"));
        Assert.Equal(HttpStatusCode.OK, corregido.Estado);

        // Un rechazo no se guarda: la misma clave se vuelve a evaluar y da el mismo rechazo.
        Assert.Equal(HttpStatusCode.BadRequest, (await PonerAsync(cliente, new { nombre = "Carlos Ruiz", telefono = "12ab" }, clave)).Estado);
    }

    [Fact]
    public async Task Con_la_organizacion_suspendida_el_cambio_es_conflicto_B()
    {
        var organizacion = (await _admin.PostearAsync("/api/tenants", new { nombre = "Transportes Suspendidos", tenantType = "Transportista" })).Id("tenantId");
        var (cliente, _) = await ConductorAsync(organizacion);

        await _admin.PostearAsync($"/api/tenants/{organizacion}/suspension");
        var cambio = await PonerAsync(cliente, new { nombre = "Carlos Ruiz", telefono = "3005555555" }, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Conflict, cambio.Estado);
        Assert.Equal("B", cambio.Texto("tipoConflicto"));
        Assert.Contains("suspendida", cambio.Texto("detail"));
        Assert.Equal("Suspendido", (await cliente.ConsultarAsync("/api/perfil")).Texto("estadoOrganizacion"));
    }

    [Fact]
    public async Task Una_clave_mal_formada_o_usada_en_otra_accion_es_conflicto_A()
    {
        var (cliente, _) = await ConductorAsync();
        var (otro, _) = await ConductorAsync();
        var clave = Guid.NewGuid();
        await PonerAsync(cliente, new { nombre = "Carlos Ruiz", telefono = (string?)null }, clave);

        var malFormada = await EnviarAsync(cliente, HttpMethod.Put, "/api/perfil", new { nombre = "Carlos Ruiz" }, "no-es-un-guid");
        var deOtroUsuario = await PonerAsync(otro, new { nombre = "Otro Usuario" }, clave);
        var enOtraAccion = await EnviarAsync(cliente, HttpMethod.Post, "/api/perfil/cierre-otras-sesiones", null, clave.ToString());

        foreach (var respuesta in new[] { malFormada, deOtroUsuario, enOtraAccion })
        {
            Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
            Assert.Equal("A", respuesta.Texto("tipoConflicto"));
        }
    }

    [Fact]
    public async Task Cerrar_otras_sesiones_revoca_las_demas_y_deja_viva_la_actual()
    {
        var (telefono, email) = await ConductorAsync();
        var otroDispositivo = await fabrica.CreateClient().IniciarSesionAsync(email, Password);
        var tercerDispositivo = await fabrica.CreateClient().IniciarSesionAsync(email, Password);
        var clave = Guid.NewGuid();

        var cierre = await EnviarAsync(telefono, HttpMethod.Post, "/api/perfil/cierre-otras-sesiones", null, clave.ToString());
        var repetido = await EnviarAsync(telefono, HttpMethod.Post, "/api/perfil/cierre-otras-sesiones", null, clave.ToString());

        Assert.Equal(HttpStatusCode.OK, cierre.Estado);
        Assert.Equal(2, cierre.Propiedad("sesionesCerradas").GetInt32());
        Assert.Equal(2, repetido.Propiedad("sesionesCerradas").GetInt32());

        using var anonimo = fabrica.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostearAsync("/api/auth/refresh", new { refreshToken = otroDispositivo.Texto("refreshToken") })).Estado);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostearAsync("/api/auth/refresh", new { refreshToken = tercerDispositivo.Texto("refreshToken") })).Estado);
        Assert.Equal(HttpStatusCode.OK, (await anonimo.PostearAsync("/api/auth/refresh", new { refreshToken = _refrescoActual })).Estado);
    }

    [Fact]
    public async Task El_token_de_usuario_lleva_el_id_de_su_sesion_y_lo_conserva_al_renovar()
    {
        using var anonimo = fabrica.CreateClient();
        var sesion = await anonimo.IniciarSesionAsync(Acceso.AdminEmail, Acceso.AdminPassword);
        var renovada = await anonimo.PostearAsync("/api/auth/refresh", new { refreshToken = sesion.Texto("refreshToken") });

        var sid = new JsonWebToken(sesion.Texto("accessToken")).GetClaim("sid").Value;
        Assert.True(Guid.TryParse(sid, out _));
        Assert.Equal(sid, new JsonWebToken(renovada.Texto("accessToken")).GetClaim("sid").Value);
    }

    [Fact]
    public async Task Un_token_de_servicio_no_tiene_perfil()
    {
        using var servicio = fabrica.CreateClient().ConToken(
            await fabrica.CreateClient().TokenServicioAsync("billing", Acceso.SecretoBilling, "ecotrace-identity"));

        Assert.Equal(HttpStatusCode.Forbidden, (await servicio.ConsultarAsync("/api/perfil")).Estado);
    }

    [Fact]
    public async Task Sin_token_el_perfil_responde_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await fabrica.CreateClient().ConsultarAsync("/api/perfil")).Estado);
    }

    private const string Password = "Conductor-2026";
    private string? _refrescoActual;

    private async Task<(HttpClient Cliente, string Email)> ConductorAsync(Guid? organizacion = null)
    {
        var email = $"conductor.{Guid.NewGuid():N}@x.co";
        await _admin.CrearUsuarioAsync(organizacion ?? _organizacion, "Conductor", email, Password);
        var sesion = await fabrica.CreateClient().IniciarSesionAsync(email, Password);
        _refrescoActual = sesion.Texto("refreshToken");
        return (fabrica.CreateClient().ConToken(sesion.Texto("accessToken")), email);
    }

    private static Task<Respuesta> PonerAsync(HttpClient cliente, object cuerpo, Guid clave) =>
        EnviarAsync(cliente, HttpMethod.Put, "/api/perfil", cuerpo, clave.ToString());

    private static async Task<Respuesta> EnviarAsync(HttpClient cliente, HttpMethod metodo, string ruta, object? cuerpo, string clave)
    {
        using var solicitud = new HttpRequestMessage(metodo, ruta) { Content = cuerpo is null ? null : JsonContent.Create(cuerpo) };
        solicitud.Headers.Add("Idempotency-Key", clave);
        return await Respuesta.De(await cliente.SendAsync(solicitud));
    }
}
