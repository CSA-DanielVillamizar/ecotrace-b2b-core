using System.Net;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

public sealed class IdentityApiTests(ApiFactory<IdentityApiMarker> fabrica) : IClassFixture<ApiFactory<IdentityApiMarker>>
{
    private readonly HttpClient _cliente = fabrica.CreateClient();

    [Fact]
    public async Task La_migracion_siembra_los_cuatro_roles_del_adr()
    {
        var roles = await _cliente.ConsultarAsync("/api/roles");

        Assert.Equal(HttpStatusCode.OK, roles.Estado);
        Assert.Equal(4, roles.Cantidad);
        var nombres = roles.Json.EnumerateArray().Select(r => r.GetProperty("nombre").GetString()!).ToArray();
        Assert.Equal(new[] { "Conductor", "Supervisor", "Administrador", "Auditor" }, nombres);
    }

    [Fact]
    public async Task Crea_una_organizacion_y_un_usuario_que_le_pertenece()
    {
        var tenant = await _cliente.PostearAsync("/api/tenants",
            new { nombre = "  Transportes del Cauca  ", tenantType = "Transportista" });

        Assert.Equal(HttpStatusCode.Created, tenant.Estado);
        Assert.Equal("Transportes del Cauca", tenant.Texto("nombre"));
        Assert.Equal("Transportista", tenant.Texto("tenantType"));

        var usuario = await _cliente.PostearAsync("/api/users", new
        {
            tenantId = tenant.Id("tenantId"),
            role = "conductor",
            nombre = "Carlos Ruiz",
            email = $"carlos.{Guid.NewGuid():N}@transportes.co"
        });

        Assert.Equal(HttpStatusCode.Created, usuario.Estado);
        Assert.Equal(tenant.Id("tenantId"), usuario.Id("tenantId"));
        Assert.Equal("Conductor", usuario.Texto("role"));
    }

    [Fact]
    public async Task Rechaza_un_usuario_de_una_organizacion_que_no_existe()
    {
        var respuesta = await _cliente.PostearAsync("/api/users", new
        {
            tenantId = Guid.NewGuid(),
            role = "Conductor",
            nombre = "Sin Organización",
            email = $"sin.org.{Guid.NewGuid():N}@x.co"
        });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Contains("no existe", respuesta.Texto("detail"));
    }

    [Fact]
    public async Task El_correo_es_unico_sin_distinguir_mayusculas()
    {
        var tenant = await _cliente.PostearAsync("/api/tenants", new { nombre = "Generadora Sur", tenantType = "Generador" });
        var correo = $"Ana.{Guid.NewGuid():N}@Empresa.co";
        object Solicitud(string email) => new { tenantId = tenant.Id("tenantId"), role = "Administrador", nombre = "Ana Gómez", email };

        var primero = await _cliente.PostearAsync("/api/users", Solicitud(correo));
        var repetido = await _cliente.PostearAsync("/api/users", Solicitud(correo.ToLowerInvariant()));

        Assert.Equal(HttpStatusCode.Created, primero.Estado);
        Assert.Equal(HttpStatusCode.Conflict, repetido.Estado);
    }

    [Fact]
    public async Task Los_errores_de_dominio_salen_como_problem_details()
    {
        var respuesta = await _cliente.PostearAsync("/api/tenants", new { nombre = "X", tenantType = "Generador" });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Equal("El dato no es válido", respuesta.Texto("title"));
        Assert.Contains("entre 2 y 120", respuesta.Texto("detail"));
    }

    [Fact]
    public async Task Falta_un_campo_obligatorio_devuelve_los_errores_por_campo()
    {
        var respuesta = await _cliente.PostearAsync("/api/tenants", new { tenantType = "Generador" });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.True(respuesta.Propiedad("errors").TryGetProperty("Nombre", out _));
    }

    [Fact]
    public async Task Un_identificador_inexistente_devuelve_404()
    {
        var respuesta = await _cliente.ConsultarAsync($"/api/tenants/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.Estado);
    }

    [Fact]
    public async Task Filtra_las_organizaciones_por_tipo()
    {
        await _cliente.PostearAsync("/api/tenants", new { nombre = "Solo Generador Uno", tenantType = "Generador" });
        await _cliente.PostearAsync("/api/tenants", new { nombre = "Solo Transportista Uno", tenantType = "Transportista" });

        var soloTransportistas = await _cliente.ConsultarAsync("/api/tenants?tipo=Transportista");

        Assert.All(soloTransportistas.Json.EnumerateArray(),
            t => Assert.Equal("Transportista", t.GetProperty("tenantType").GetString()));
    }

    [Fact]
    public async Task El_endpoint_de_salud_confirma_la_base_de_datos()
    {
        var salud = await _cliente.ConsultarAsync("/health");

        Assert.Equal(HttpStatusCode.OK, salud.Estado);
        Assert.Equal("ok", salud.Texto("baseDeDatos"));
    }
}
