using System.Net;
using System.Security.Cryptography;
using EcoTrace.Tests.TestSupport;
using Microsoft.IdentityModel.Tokens;

namespace EcoTrace.Tests;

/// <summary>
/// Pruebas de ataque del Trabajo 3 contra Fleet Management como Resource Server. Fleet valida el token con la
/// clave pública del Identity real (en memoria): firma, exp, iss y aud. Cualquier token que no sirve se
/// rechaza con 401 y un cuerpo genérico; un token válido sin permiso, con 403.
/// </summary>
public sealed class AtaquesTokenFleetTests(FleetConIdentity fleet) : IClassFixture<FleetConIdentity>, IAsyncLifetime
{
    private const string RutaProtegida = "/api/vehiculos";

    private Guid _transportista;
    private string _tokenSupervisor = null!;
    private string _tokenConductor = null!;
    private string _tokenServicio = null!;
    private Guid _vehiculoPropio;
    private Guid _vehiculoDeOtra;
    private Guid _conductorDeOtra;

    public async Task InitializeAsync()
    {
        using var admin = await Acceso.ClienteAdministradorAsync(fleet.ClienteIdentity);
        _transportista = (await admin.PostearAsync("/api/tenants", new { nombre = "Transportes Fleet", tenantType = "Transportista" })).Id("tenantId");
        var otraOrganizacion = (await admin.PostearAsync("/api/tenants", new { nombre = "Transportes Otros", tenantType = "Transportista" })).Id("tenantId");

        // Tokens reales, emitidos por Identity con login o con credenciales de servicio.
        var sufijo = Guid.NewGuid().ToString("N");
        await admin.CrearUsuarioAsync(_transportista, "Supervisor", $"supervisor.{sufijo}@x.co", "Supervisor-2026");
        await admin.CrearUsuarioAsync(_transportista, "Conductor", $"conductor.{sufijo}@x.co", "Conductor-2026");
        _tokenSupervisor = await admin.TokenDeAsync($"supervisor.{sufijo}@x.co", "Supervisor-2026");
        _tokenConductor = await admin.TokenDeAsync($"conductor.{sufijo}@x.co", "Conductor-2026");
        _tokenServicio = await admin.TokenServicioAsync("billing", Acceso.SecretoBilling, "ecotrace-fleet");

        using var supervisor = fleet.ClienteFleet(_tokenSupervisor);
        _vehiculoPropio = (await supervisor.PostearAsync("/api/vehiculos", new { placa = Placa(), capacidadKg = 5000 })).Id("vehiculoId");

        using var supervisorDeOtra = fleet.ClienteFleet(FleetConIdentity.Token(otraOrganizacion));
        _vehiculoDeOtra = (await supervisorDeOtra.PostearAsync("/api/vehiculos", new { placa = Placa(), capacidadKg = 5000 })).Id("vehiculoId");
        _conductorDeOtra = (await supervisorDeOtra.PostearAsync("/api/conductores",
            new { nombre = "Luis Mora", licencia = $"LIC-{sufijo[..6]}" })).Id("conductorId");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ---- 401: el token no sirve ----------------------------------------------------------------

    [Fact]
    public async Task Un_token_real_de_Identity_pasa()
    {
        Assert.Equal(HttpStatusCode.OK, await EstadoConAsync(_tokenSupervisor));
    }

    [Fact]
    public async Task Sin_token_responde_401()
    {
        using var cliente = fleet.ClienteFleet();

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.ConsultarAsync(RutaProtegida)).Estado);
    }

    [Fact]
    public async Task Un_token_vencido_responde_401()
    {
        // Vencido hace más que la tolerancia de reloj (30 s).
        var vencido = Acceso.Forjar(_transportista, "Supervisor", audiencias: ["ecotrace-fleet"], expira: DateTime.UtcNow.AddMinutes(-2));

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoConAsync(vencido));
    }

    [Fact]
    public async Task Una_firma_alterada_en_un_byte_responde_401()
    {
        var partes = _tokenSupervisor.Split('.');
        var firma = Base64UrlEncoder.DecodeBytes(partes[2]);
        firma[firma.Length / 2] ^= 0x01;
        var alterado = $"{partes[0]}.{partes[1]}.{Base64UrlEncoder.Encode(firma)}";

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoConAsync(alterado));
    }

    [Fact]
    public async Task Un_token_con_alg_none_responde_401()
    {
        // Mismo contenido que un token real, pero sin firma y diciendo que no hace falta.
        var payload = _tokenSupervisor.Split('.')[1];
        var encabezado = Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""");

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoConAsync($"{encabezado}.{payload}."));
    }

    [Fact]
    public async Task Un_token_de_otro_entorno_responde_401()
    {
        var deProduccion = Acceso.Forjar(_transportista, "Supervisor",
            emisor: "https://identity.ecotrace.local/produccion", audiencias: ["ecotrace-fleet"]);

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoConAsync(deProduccion));
    }

    [Fact]
    public async Task Un_token_para_otro_modulo_responde_401()
    {
        var soloParaBilling = Acceso.Forjar(_transportista, "Supervisor", audiencias: ["ecotrace-billing"]);

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoConAsync(soloParaBilling));
    }

    [Fact]
    public async Task Un_token_firmado_con_otra_clave_responde_401()
    {
        using var otraClave = RSA.Create(2048);
        var forjado = Acceso.Forjar(_transportista, "Supervisor", audiencias: ["ecotrace-fleet"],
            credenciales: new SigningCredentials(new RsaSecurityKey(otraClave), SecurityAlgorithms.RsaSha256));

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoConAsync(forjado));
    }

    [Fact]
    public async Task El_401_es_generico_y_no_explica_el_motivo()
    {
        var vencido = await ConsultarConAsync(
            Acceso.Forjar(_transportista, "Supervisor", audiencias: ["ecotrace-fleet"], expira: DateTime.UtcNow.AddMinutes(-2)));
        var otraAudiencia = await ConsultarConAsync(Acceso.Forjar(_transportista, "Supervisor", audiencias: ["ecotrace-cargo"]));

        Assert.Equal(vencido.Texto("title"), otraAudiencia.Texto("title"));
        Assert.Equal(vencido.Texto("detail"), otraAudiencia.Texto("detail"));
        Assert.DoesNotContain("IDX", vencido.Json.GetRawText());
        Assert.DoesNotContain("expired", vencido.Json.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Valida_sin_llamar_a_Identity_en_cada_solicitud_y_sigue_aunque_Identity_se_caiga()
    {
        // Un Fleet aparte, para contar desde cero las veces que pide el JWKS.
        using var aislado = new FleetConIdentity();
        var token = FleetConIdentity.Token(Guid.NewGuid());
        using var cliente = aislado.ClienteFleet(token);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await cliente.ConsultarAsync(RutaProtegida)).Estado);
        }

        Assert.Equal(1, aislado.JwksDesdeFleet.Llamadas);

        aislado.JwksDesdeFleet.Caido = true;
        Assert.Equal(HttpStatusCode.OK, (await cliente.ConsultarAsync(RutaProtegida)).Estado);
    }

    // ---- 403: el token sirve, pero no tiene permiso ------------------------------------------

    [Fact]
    public async Task Un_token_valido_de_otra_organizacion_responde_403()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await ConsultarConAsync(_tokenSupervisor, $"/api/vehiculos/{_vehiculoDeOtra}")).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await ConsultarConAsync(_tokenConductor, $"/api/vehiculos/{_vehiculoDeOtra}")).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await ConsultarConAsync(_tokenSupervisor, $"/api/conductores/{_conductorDeOtra}")).Estado);
    }

    [Fact]
    public async Task La_organizacion_sale_del_token_aunque_se_pida_otra_por_parametro()
    {
        var lista = await ConsultarConAsync(_tokenSupervisor, $"/api/vehiculos?tenantId={Guid.NewGuid()}");

        var vehiculos = lista.Json.EnumerateArray().Select(v => v.GetProperty("vehiculoId").GetGuid()).ToList();
        Assert.Contains(_vehiculoPropio, vehiculos);
        Assert.DoesNotContain(_vehiculoDeOtra, vehiculos);
    }

    [Fact]
    public async Task Un_token_valido_con_un_rol_sin_permiso_responde_403()
    {
        using var conductor = fleet.ClienteFleet(_tokenConductor);
        using var supervisor = fleet.ClienteFleet(_tokenSupervisor);
        using var servicio = fleet.ClienteFleet(_tokenServicio);

        // El conductor no registra flota, una persona no llama rutas internas y un servicio no registra vehículos.
        var conductorRegistraVehiculo = await conductor.PostearAsync("/api/vehiculos", new { placa = Placa(), capacidadKg = 1000 });
        var personaReserva = await supervisor.PostearAsync("/api/reservas",
            new { cargaId = Guid.NewGuid(), vehiculoId = _vehiculoPropio, conductorId = Guid.NewGuid() });
        var personaArmaFallos = await supervisor.PostearAsync("/api/_simulacion/fallos", new { operacion = "liberaciones", cantidad = 1 });
        var servicioRegistraVehiculo = await servicio.PostearAsync("/api/vehiculos", new { placa = Placa(), capacidadKg = 1000 });

        Assert.Equal(HttpStatusCode.Forbidden, conductorRegistraVehiculo.Estado);
        Assert.Equal(HttpStatusCode.Forbidden, personaReserva.Estado);
        Assert.Equal(HttpStatusCode.Forbidden, personaArmaFallos.Estado);
        Assert.Equal(HttpStatusCode.Forbidden, servicioRegistraVehiculo.Estado);
    }

    [Fact]
    public async Task Cada_quien_lee_lo_que_le_toca()
    {
        // El conductor ve el vehículo de su organización y el Saga (token de servicio) ve el de cualquiera.
        Assert.Equal(HttpStatusCode.OK, (await ConsultarConAsync(_tokenConductor, $"/api/vehiculos/{_vehiculoPropio}")).Estado);
        Assert.Equal(HttpStatusCode.OK, (await ConsultarConAsync(_tokenServicio, $"/api/vehiculos/{_vehiculoDeOtra}")).Estado);
    }

    private static string Placa() => "A" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private async Task<HttpStatusCode> EstadoConAsync(string token) => (await ConsultarConAsync(token)).Estado;

    private async Task<Respuesta> ConsultarConAsync(string token, string ruta = RutaProtegida)
    {
        using var cliente = fleet.ClienteFleet(token);
        return await cliente.ConsultarAsync(ruta);
    }
}
