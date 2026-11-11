using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

/// <summary>
/// La seguridad de Cargo &amp; Tracking como Resource Server (ADR 0004). Primero los ataques a los tokens que pide la
/// especificación, que deben rechazarse con 401 o 403, y después las reglas de rol y de organización: qué puede hacer
/// cada persona y sobre qué cargas. Las pruebas arman sus propios tokens con la clave del Identity de prueba, porque
/// necesitan justo lo que ningún cliente real podría mandar.
/// </summary>
public sealed class SeguridadCargoTests : IAsyncLifetime
{
    private readonly ApiFactory<CargoTrackingApiMarker> _fabrica = new();
    private readonly Guid _generador = Guid.NewGuid();
    private readonly Guid _transportista = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    private HttpClient Como(string rol, Guid? organizacion = null) =>
        _fabrica.CreateClient().ConToken(SeguridadDePrueba.Emitir(rol, organizacion));

    private HttpClient ConToken(string? token) => _fabrica.CreateClient().ConToken(token);

    private object NuevaCarga(Guid? generador = null) => new
    {
        generadorTenantId = generador ?? _generador,
        transportistaTenantId = _transportista,
        descripcion = "Residuos industriales no peligrosos",
        origen = "Cali",
        destino = "Popayán",
        pesoKg = 4200
    };

    private static object Entrega() => new { estado = "Entregado", ubicacion = "Popayán" };

    private async Task<Guid> CrearCargaAsync()
    {
        var respuesta = await Como(Rol.Supervisor, _generador).PostearAsync("/api/cargas", NuevaCarga());
        Assert.Equal(HttpStatusCode.Created, respuesta.Estado);
        return respuesta.Id("cargaId");
    }

    private async Task<Guid> CargaEnTransitoAsync()
    {
        var cargaId = await CrearCargaAsync();
        var asignada = await Como(Rol.Supervisor, _transportista).PostearAsync(
            $"/api/cargas/{cargaId}/asignacion", new { vehiculoId = Guid.NewGuid(), conductorId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Created, asignada.Estado);
        var enTransito = await Como(Rol.Conductor, _transportista).PostearAsync(
            $"/api/cargas/{cargaId}/seguimientos", new { estado = "EnTransito", ubicacion = "Santander de Quilichao" });
        Assert.Equal(HttpStatusCode.Created, enTransito.Estado);
        return cargaId;
    }

    private static async Task<HttpStatusCode> EstadoDeAsync(HttpClient cliente, string ruta) =>
        (await cliente.GetAsync(ruta)).StatusCode;

    // ------------------------------------------------------------------ ataques a los tokens (los 8 de la especificación)

    [Fact]
    public async Task Sin_token_responde_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoDeAsync(ConToken(null), "/api/cargas"));
    }

    [Fact]
    public async Task Un_token_vencido_responde_401()
    {
        var vencido = SeguridadDePrueba.Emitir(
            Rol.Supervisor, _transportista, emitido: DateTime.UtcNow.AddMinutes(-20), expira: DateTime.UtcNow.AddMinutes(-5));

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoDeAsync(ConToken(vencido), "/api/cargas"));
    }

    [Fact]
    public async Task Un_token_con_la_firma_alterada_responde_401()
    {
        var alterado = SeguridadDePrueba.ConFirmaAlterada(SeguridadDePrueba.Emitir(Rol.Supervisor, _transportista));

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoDeAsync(ConToken(alterado), "/api/cargas"));
    }

    [Fact]
    public async Task Un_token_con_alg_none_responde_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoDeAsync(ConToken(SeguridadDePrueba.EmitirSinFirma()), "/api/cargas"));
    }

    [Fact]
    public async Task Un_token_de_otro_entorno_responde_401()
    {
        var deOtroEntorno = SeguridadDePrueba.Emitir(Rol.Supervisor, _transportista, emisor: "https://identity.ecotrace.local/desarrollo");

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoDeAsync(ConToken(deOtroEntorno), "/api/cargas"));
    }

    [Fact]
    public async Task Un_token_para_otro_modulo_responde_401()
    {
        var paraFleet = SeguridadDePrueba.Emitir(Rol.Supervisor, _transportista, audiencias: [SeguridadDePrueba.AudFleet]);

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoDeAsync(ConToken(paraFleet), "/api/cargas"));
    }

    [Fact]
    public async Task Un_token_valido_de_otra_organizacion_responde_403()
    {
        var cargaId = await CrearCargaAsync();
        var deOtraOrganizacion = Como(Rol.Supervisor, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, await EstadoDeAsync(deOtraOrganizacion, $"/api/cargas/{cargaId}"));
        Assert.Equal(HttpStatusCode.Forbidden, await EstadoDeAsync(deOtraOrganizacion, $"/api/cargas/{cargaId}/seguimientos"));
    }

    [Fact]
    public async Task Un_token_valido_con_un_rol_sin_permiso_responde_403()
    {
        var cargaId = await CrearCargaAsync();

        // Asignar es de un supervisor o un administrador; un conductor de la misma organización no puede.
        var asignar = await Como(Rol.Conductor, _transportista).PostearAsync(
            $"/api/cargas/{cargaId}/asignacion", new { vehiculoId = Guid.NewGuid(), conductorId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Forbidden, asignar.Estado);
    }

    // ------------------------------------------------------------------ lo que se le cuenta al atacante

    [Fact]
    public async Task El_401_y_el_403_son_genericos_y_no_dicen_por_que_fallo_el_token()
    {
        var vencido = SeguridadDePrueba.Emitir(
            Rol.Supervisor, _transportista, emitido: DateTime.UtcNow.AddMinutes(-20), expira: DateTime.UtcNow.AddMinutes(-5));
        var cargaId = await CrearCargaAsync();

        var noAutenticado = await Respuesta.De(await ConToken(vencido).GetAsync("/api/cargas"));
        var sinPermiso = await Respuesta.De(await Como(Rol.Supervisor, Guid.NewGuid()).GetAsync($"/api/cargas/{cargaId}"));

        Assert.Equal("Falta un token válido.", noAutenticado.Texto("detail"));
        Assert.Equal("No tienes permiso para esta operación.", sinPermiso.Texto("detail"));
    }

    [Fact]
    public async Task Un_token_firmado_con_otra_clave_responde_401_aunque_diga_el_kid_correcto()
    {
        using var otraClave = RSA.Create(2048);
        var falso = SeguridadDePrueba.Emitir(Rol.Supervisor, _transportista, firmarCon: otraClave);

        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoDeAsync(ConToken(falso), "/api/cargas"));
    }

    // ------------------------------------------------------------------ la clave pública de Identity

    [Fact]
    public async Task La_clave_publica_se_baja_una_vez_y_no_en_cada_solicitud()
    {
        var cliente = Como(Rol.Supervisor, _transportista);
        Assert.Equal(HttpStatusCode.OK, await EstadoDeAsync(cliente, "/api/cargas"));
        var descargas = _fabrica.IdentityDeMentira.LlamadasJwks;

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await EstadoDeAsync(cliente, "/api/cargas"));
        }

        Assert.Equal(descargas, _fabrica.IdentityDeMentira.LlamadasJwks);
    }

    [Fact]
    public async Task Si_Identity_se_cae_un_token_valido_sigue_validandose()
    {
        var cliente = Como(Rol.Supervisor, _transportista);
        Assert.Equal(HttpStatusCode.OK, await EstadoDeAsync(cliente, "/api/cargas"));

        _fabrica.IdentityDeMentira.Caido = true;

        Assert.Equal(HttpStatusCode.OK, await EstadoDeAsync(cliente, "/api/cargas"));
    }

    // ------------------------------------------------------------------ quién puede hacer qué

    [Fact]
    public async Task Una_carga_la_crea_alguien_de_la_organizacion_generadora_y_no_de_otra()
    {
        var deOtra = await Como(Rol.Supervisor, _generador).PostearAsync("/api/cargas", NuevaCarga(generador: Guid.NewGuid()));
        var conductor = await Como(Rol.Conductor, _generador).PostearAsync("/api/cargas", NuevaCarga());
        var propia = await Como(Rol.Administrador, _generador).PostearAsync("/api/cargas", NuevaCarga());

        Assert.Equal(HttpStatusCode.Forbidden, deOtra.Estado);
        Assert.Equal(HttpStatusCode.Forbidden, conductor.Estado);
        Assert.Equal(HttpStatusCode.Created, propia.Estado);
    }

    [Fact]
    public async Task Confirmar_la_entrega_la_hace_un_conductor_o_un_supervisor_del_transportista_de_esa_carga()
    {
        var cargaId = await CargaEnTransitoAsync();
        var ruta = $"/api/cargas/{cargaId}/seguimientos";
        var servicio = SeguridadDePrueba.Emitir(Rol.Servicio, sub: "billing");

        // Ninguno de estos puede, y ninguno cambia el estado de la carga.
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(Rol.Auditor, _transportista).PostearAsync(ruta, Entrega())).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(Rol.Administrador, _transportista).PostearAsync(ruta, Entrega())).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(Rol.Conductor, Guid.NewGuid()).PostearAsync(ruta, Entrega())).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await Como(Rol.Supervisor, _generador).PostearAsync(ruta, Entrega())).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await ConToken(servicio).PostearAsync(ruta, Entrega())).Estado);

        // El conductor del transportista sí.
        Assert.Equal(HttpStatusCode.Created, (await Como(Rol.Conductor, _transportista).PostearAsync(ruta, Entrega())).Estado);
    }

    [Fact]
    public async Task Un_supervisor_del_transportista_tambien_puede_confirmar_la_entrega()
    {
        var cargaId = await CargaEnTransitoAsync();

        var entrega = await Como(Rol.Supervisor, _transportista).PostearAsync($"/api/cargas/{cargaId}/seguimientos", Entrega());

        Assert.Equal(HttpStatusCode.Created, entrega.Estado);
    }

    [Fact]
    public async Task Repetir_una_entrega_con_la_misma_clave_solo_se_le_responde_a_la_organizacion_de_la_carga()
    {
        var cargaId = await CargaEnTransitoAsync();
        var ruta = $"/api/cargas/{cargaId}/seguimientos";
        var clave = Guid.NewGuid().ToString();

        async Task<HttpStatusCode> EntregarConAsync(HttpClient cliente)
        {
            using var solicitud = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(Entrega()) };
            solicitud.Headers.Add("Idempotency-Key", clave);
            return (await cliente.SendAsync(solicitud)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.Created, await EntregarConAsync(Como(Rol.Conductor, _transportista)));
        Assert.Equal(HttpStatusCode.OK, await EntregarConAsync(Como(Rol.Conductor, _transportista)));

        // Quien no es de la organización transportista no recibe el resultado guardado aunque conozca la clave.
        Assert.Equal(HttpStatusCode.Forbidden, await EntregarConAsync(Como(Rol.Conductor, Guid.NewGuid())));
    }

    [Fact]
    public async Task La_lista_trae_solo_las_cargas_de_la_organizacion_del_token()
    {
        await CrearCargaAsync();

        var comoGenerador = await Como(Rol.Auditor, _generador).ConsultarAsync("/api/cargas");
        var comoTransportista = await Como(Rol.Conductor, _transportista).ConsultarAsync("/api/cargas");
        var otraOrganizacion = await Como(Rol.Supervisor, Guid.NewGuid()).ConsultarAsync("/api/cargas");

        Assert.Equal(1, comoGenerador.Cantidad);
        Assert.Equal(1, comoTransportista.Cantidad);
        Assert.Equal(0, otraOrganizacion.Cantidad);
    }

    [Fact]
    public async Task Pedir_las_cargas_de_otra_organizacion_o_listar_sin_organizacion_responde_403()
    {
        var otra = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, await EstadoDeAsync(Como(Rol.Supervisor, _generador), $"/api/cargas?tenantId={otra}"));

        // Un servicio no tiene organización: no se le entrega «todo», se le niega.
        var servicio = SeguridadDePrueba.Emitir(Rol.Servicio, sub: "billing");
        Assert.Equal(HttpStatusCode.Forbidden, await EstadoDeAsync(ConToken(servicio), "/api/cargas"));
    }

    [Fact]
    public async Task Cualquier_rol_de_las_organizaciones_de_la_carga_puede_consultarla()
    {
        var cargaId = await CrearCargaAsync();

        Assert.Equal(HttpStatusCode.OK, await EstadoDeAsync(Como(Rol.Auditor, _generador), $"/api/cargas/{cargaId}"));
        Assert.Equal(HttpStatusCode.OK, await EstadoDeAsync(Como(Rol.Conductor, _transportista), $"/api/cargas/{cargaId}"));
    }

    [Fact]
    public async Task El_Outbox_es_solo_de_los_servicios_internos()
    {
        var servicio = SeguridadDePrueba.Emitir(Rol.Servicio, sub: "operaciones");

        Assert.Equal(HttpStatusCode.OK, await EstadoDeAsync(ConToken(servicio), "/api/outbox"));
        Assert.Equal(HttpStatusCode.Forbidden, await EstadoDeAsync(Como(Rol.Administrador, _transportista), "/api/outbox"));
        Assert.Equal(HttpStatusCode.Unauthorized, await EstadoDeAsync(ConToken(null), "/api/outbox"));
    }

    [Fact]
    public async Task La_salud_y_la_descripcion_del_contexto_no_piden_token()
    {
        Assert.Equal(HttpStatusCode.OK, await EstadoDeAsync(ConToken(null), "/health"));
        Assert.Equal(HttpStatusCode.OK, await EstadoDeAsync(ConToken(null), "/api/_meta/contexto"));
    }
}
