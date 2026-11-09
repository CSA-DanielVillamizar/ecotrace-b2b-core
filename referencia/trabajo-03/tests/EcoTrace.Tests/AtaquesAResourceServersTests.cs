using System.Net;
using System.Security.Cryptography;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

/// <summary>
/// Las pruebas de ataque del ADR 0004 / especificación del Trabajo 3: cada Resource Server rechaza de forma limpia un token
/// ausente, vencido, con la firma alterada, sin firma (<c>alg: none</c>), de otro entorno o de otro módulo (401), y un token
/// válido que no tiene permiso, sea por la organización o por el rol (403). Son las mismas para Fleet, Cargo y Billing:
/// cada uno solo dice cuál es su ruta protegida, cómo crea un recurso y qué operación exige un rol.
/// </summary>
public abstract class AtaquesAUnResourceServer<TMarker>(ApiFactory<TMarker> fabrica) where TMarker : class
{
    /// <summary>Una ruta que cualquier persona con un token válido de una organización puede pedir.</summary>
    protected abstract string RutaDeLectura { get; }

    protected string Audiencia => ClientesDePrueba.AudienciaDe<TMarker>();

    /// <summary>La audiencia de otro módulo: un token para ese módulo no sirve aquí.</summary>
    protected abstract string AudienciaAjena { get; }

    /// <summary>Crea un recurso de la organización de <paramref name="dueno"/> y devuelve la ruta para leerlo.</summary>
    protected abstract Task<string> CrearRecursoAsync(ParDeOrganizaciones par);

    /// <summary>Una operación que exige un rol y la hace alguien de la misma organización pero con un rol sin permiso.</summary>
    protected abstract Task<Respuesta> OperacionConRolSinPermisoAsync(ParDeOrganizaciones par, string ruta);

    /// <summary>Una ruta interna (solo para el rol Servicio) con un cuerpo válido. Nulo si el módulo no ofrece ninguna.</summary>
    protected abstract (HttpMethod Metodo, string Ruta, object? Cuerpo)? RutaInterna { get; }

    private HttpClient ConToken(string? token) => fabrica.CreateClient().ConToken(token);

    private async Task<HttpStatusCode> LeerConAsync(string? token) => (await ConToken(token).GetAsync(RutaDeLectura)).StatusCode;

    private static string TokenValido(Guid? tenantId = null, string rol = Rol.Supervisor) =>
        SeguridadDePrueba.Emitir(rol, tenantId ?? Guid.NewGuid());

    // -------------------------------------------------------------- 401

    [Fact]
    public async Task Sin_token_responde_401()
    {
        var respuesta = await ConToken(null).GetAsync(RutaDeLectura);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    public async Task Un_token_vencido_responde_401()
    {
        var ahora = DateTime.UtcNow;
        var vencido = SeguridadDePrueba.Emitir(Rol.Supervisor, Guid.NewGuid(), emitido: ahora.AddMinutes(-30), expira: ahora.AddMinutes(-10));

        Assert.Equal(HttpStatusCode.Unauthorized, await LeerConAsync(vencido));
    }

    [Fact]
    public async Task Un_token_con_la_firma_alterada_responde_401()
    {
        var alterado = SeguridadDePrueba.ConFirmaAlterada(TokenValido());

        Assert.Equal(HttpStatusCode.Unauthorized, await LeerConAsync(alterado));
    }

    [Fact]
    public async Task Un_token_sin_firma_alg_none_responde_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await LeerConAsync(SeguridadDePrueba.EmitirSinFirma()));
    }

    [Fact]
    public async Task Un_token_de_otro_entorno_iss_distinto_responde_401()
    {
        var deProduccion = SeguridadDePrueba.Emitir(Rol.Supervisor, Guid.NewGuid(), issuer: "ecotrace-identity-produccion");

        Assert.Equal(HttpStatusCode.Unauthorized, await LeerConAsync(deProduccion));
    }

    [Fact]
    public async Task Un_token_para_otro_modulo_aud_distinto_responde_401()
    {
        var paraOtro = SeguridadDePrueba.Emitir(Rol.Supervisor, Guid.NewGuid(), audiencias: [AudienciaAjena]);

        Assert.Equal(HttpStatusCode.Unauthorized, await LeerConAsync(paraOtro));
    }

    [Fact]
    public async Task Un_token_firmado_con_otra_clave_responde_401_aunque_diga_ser_de_Identity()
    {
        using var atacante = RSA.Create(2048);

        // Mismo kid que la clave de Identity y claims perfectos: lo único falso es la clave con la que se firmó.
        var falsificado = SeguridadDePrueba.Emitir(Rol.Administrador, Guid.NewGuid(), plataforma: true, firmarCon: atacante);

        Assert.Equal(HttpStatusCode.Unauthorized, await LeerConAsync(falsificado));
    }

    [Fact]
    public async Task Un_token_con_un_kid_desconocido_responde_401()
    {
        using var atacante = RSA.Create(2048);
        var conKidInventado = SeguridadDePrueba.Emitir(Rol.Administrador, Guid.NewGuid(), plataforma: true, firmarCon: atacante, kid: "inventado");

        Assert.Equal(HttpStatusCode.Unauthorized, await LeerConAsync(conKidInventado));
    }

    [Fact]
    public async Task El_401_es_generico_y_no_dice_por_que_fallo_el_token()
    {
        var ahora = DateTime.UtcNow;
        var vencido = SeguridadDePrueba.Emitir(Rol.Supervisor, Guid.NewGuid(), emitido: ahora.AddMinutes(-30), expira: ahora.AddMinutes(-10));

        var respuesta = await Respuesta.De(await ConToken(vencido).GetAsync(RutaDeLectura));
        var textoCompleto = respuesta.Json.GetRawText().ToLowerInvariant();

        Assert.Equal("No autenticado", respuesta.Texto("title"));
        foreach (var delator in new[] { "expir", "signature", "firma", "lifetime", "audience", "issuer", "idx" })
        {
            Assert.DoesNotContain(delator, textoCompleto);
        }
    }

    // -------------------------------------------------------------- 403

    [Fact]
    public async Task Un_token_valido_de_otra_organizacion_responde_403()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var ruta = await CrearRecursoAsync(par);

        var intruso = fabrica.Como(new Persona(Guid.NewGuid(), Rol.Administrador));
        var dueno = fabrica.Como(par.SupervisorTransportista);

        Assert.Equal(HttpStatusCode.Forbidden, (await intruso.GetAsync(ruta)).StatusCode);
        // Y el dueño sí lo ve: el 403 es por la organización, no porque la ruta esté rota.
        Assert.Equal(HttpStatusCode.OK, (await dueno.GetAsync(ruta)).StatusCode);
    }

    [Fact]
    public async Task Un_token_valido_con_un_rol_sin_permiso_responde_403()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var ruta = await CrearRecursoAsync(par);

        var respuesta = await OperacionConRolSinPermisoAsync(par, ruta);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.Estado);
        Assert.Equal("Sin permiso", respuesta.Texto("title"));
    }

    [Fact]
    public async Task Las_listas_se_filtran_con_la_organizacion_del_token_y_no_con_lo_que_pida_el_cliente()
    {
        var par = ParDeOrganizaciones.Nuevo();
        await CrearRecursoAsync(par);
        var otro = ParDeOrganizaciones.Nuevo();
        await CrearRecursoAsync(otro);

        var propias = await Respuesta.De(await fabrica.Como(par.SupervisorTransportista).GetAsync(RutaDeLectura));
        var pidiendoLasAjenas = await fabrica.Como(par.SupervisorTransportista)
            .GetAsync($"{RutaDeLectura}?tenantId={otro.TransportistaId}");

        Assert.Equal(HttpStatusCode.OK, propias.Estado);
        Assert.Equal(HttpStatusCode.Forbidden, pidiendoLasAjenas.StatusCode);
    }

    [Fact]
    public async Task Un_token_de_servicio_no_sirve_para_listar_datos_de_las_organizaciones()
    {
        var servicio = fabrica.Como(Actor.ServicioInterno);

        Assert.Equal(HttpStatusCode.Forbidden, (await servicio.GetAsync(RutaDeLectura)).StatusCode);
    }

    [Fact]
    public async Task Las_rutas_internas_son_solo_para_el_rol_Servicio()
    {
        if (RutaInterna is not var (metodo, ruta, cuerpo))
        {
            return; // Cargo no ofrece rutas de servicio.
        }

        var par = ParDeOrganizaciones.Nuevo();

        async Task<HttpStatusCode> LlamarAsync(HttpClient cliente)
        {
            using var solicitud = new HttpRequestMessage(metodo, ruta);
            if (cuerpo is not null)
            {
                solicitud.Content = System.Net.Http.Json.JsonContent.Create(cuerpo);
            }

            return (await cliente.SendAsync(solicitud)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.Forbidden, await LlamarAsync(fabrica.Como(par.AdminGenerador)));
        Assert.Equal(HttpStatusCode.Forbidden, await LlamarAsync(fabrica.Como(par.AdminTransportista)));
        Assert.Equal(HttpStatusCode.Forbidden, await LlamarAsync(fabrica.Como(Actor.Operador)));
        Assert.Equal(HttpStatusCode.Unauthorized, await LlamarAsync(ConToken(null)));

        // Un token de servicio emitido para otro módulo tampoco entra, ni el emitido para este con un rol de persona.
        var paraOtroModulo = ConToken(SeguridadDePrueba.EmitirServicio(AudienciaAjena));
        Assert.Equal(HttpStatusCode.Unauthorized, await LlamarAsync(paraOtroModulo));

        // El de este módulo sí: no puede ser un 401 ni un 403.
        var estado = await LlamarAsync(ConToken(SeguridadDePrueba.EmitirServicio(Audiencia)));
        Assert.NotEqual(HttpStatusCode.Unauthorized, estado);
        Assert.NotEqual(HttpStatusCode.Forbidden, estado);
    }

    [Fact]
    public async Task Las_rutas_de_salud_y_de_contexto_siguen_abiertas()
    {
        var anonimo = ConToken(null);

        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync("/api/_meta/contexto")).StatusCode);
    }
}

public sealed class AtaquesAFleetTests(ApiFactory<FleetManagementApiMarker> fabrica)
    : AtaquesAUnResourceServer<FleetManagementApiMarker>(fabrica), IClassFixture<ApiFactory<FleetManagementApiMarker>>
{
    private readonly ApiFactory<FleetManagementApiMarker> _fabrica = fabrica;

    protected override string RutaDeLectura => "/api/vehiculos";

    protected override string AudienciaAjena => SeguridadDePrueba.AudBilling;

    protected override (HttpMethod, string, object?)? RutaInterna =>
        (HttpMethod.Post, "/api/reservas", new { cargaId = Guid.NewGuid(), vehiculoId = Guid.NewGuid(), conductorId = Guid.NewGuid() });

    protected override async Task<string> CrearRecursoAsync(ParDeOrganizaciones par)
    {
        var supervisor = par.SupervisorTransportista;
        var vehiculo = await _fabrica.Como(supervisor).PostearAsync("/api/vehiculos", new
        {
            tenantId = par.TransportistaId, registradoPorUserId = supervisor.UserId,
            placa = "A" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(), capacidadKg = 5000
        });
        Assert.Equal(HttpStatusCode.Created, vehiculo.Estado);
        return $"/api/vehiculos/{vehiculo.Id("vehiculoId")}";
    }

    protected override Task<Respuesta> OperacionConRolSinPermisoAsync(ParDeOrganizaciones par, string ruta) =>
        // Un conductor de la misma organización no registra vehículos: eso es del supervisor o del administrador.
        _fabrica.Como(par.ConductorTransportista).PostearAsync("/api/vehiculos", new
        {
            tenantId = par.TransportistaId, registradoPorUserId = par.ConductorTransportista.UserId,
            placa = "B" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(), capacidadKg = 5000
        });

    [Fact]
    public async Task Quien_registra_un_vehiculo_es_el_del_token_y_no_el_que_diga_el_cuerpo()
    {
        var supervisor = new Persona(Guid.NewGuid(), Rol.Supervisor);

        var suplantando = await _fabrica.Como(supervisor).PostearAsync("/api/vehiculos", new
        {
            tenantId = supervisor.TenantId, registradoPorUserId = Guid.NewGuid(), placa = "C12345", capacidadKg = 1000
        });
        var organizacionAjena = await _fabrica.Como(supervisor).PostearAsync("/api/vehiculos", new
        {
            tenantId = Guid.NewGuid(), registradoPorUserId = supervisor.UserId, placa = "C54321", capacidadKg = 1000
        });

        Assert.Equal(HttpStatusCode.Forbidden, suplantando.Estado);
        Assert.Equal(HttpStatusCode.Forbidden, organizacionAjena.Estado);
    }
}

public sealed class AtaquesACargoTests(ApiFactory<CargoTrackingApiMarker> fabrica)
    : AtaquesAUnResourceServer<CargoTrackingApiMarker>(fabrica), IClassFixture<ApiFactory<CargoTrackingApiMarker>>
{
    private readonly ApiFactory<CargoTrackingApiMarker> _fabrica = fabrica;

    protected override string RutaDeLectura => "/api/cargas";

    protected override string AudienciaAjena => SeguridadDePrueba.AudFleet;

    // Cargo no ofrece rutas de servicio: lo que tiene de interno es el Outbox, que es del operador (prueba más abajo).
    protected override (HttpMethod, string, object?)? RutaInterna => null;

    protected override async Task<string> CrearRecursoAsync(ParDeOrganizaciones par)
    {
        var carga = await _fabrica.Como(par.AdminGenerador).PostearAsync("/api/cargas", new
        {
            generadorTenantId = par.GeneradorId, transportistaTenantId = par.TransportistaId,
            descripcion = "Residuos industriales no peligrosos", origen = "Cali", destino = "Popayán", pesoKg = 4200
        });
        Assert.Equal(HttpStatusCode.Created, carga.Estado);
        return $"/api/cargas/{carga.Id("cargaId")}";
    }

    protected override Task<Respuesta> OperacionConRolSinPermisoAsync(ParDeOrganizaciones par, string ruta) =>
        // Confirmar una entrega es del conductor o del supervisor del transportista: su administrador no lo hace.
        _fabrica.Como(par.AdminTransportista).PostearAsync($"{ruta}/seguimientos", new { estado = "Entregado", ubicacion = "Popayán" });

    [Fact]
    public async Task El_Outbox_es_solo_del_operador_de_la_plataforma()
    {
        var par = ParDeOrganizaciones.Nuevo();

        Assert.Equal(HttpStatusCode.Forbidden, (await _fabrica.Como(par.AdminGenerador).GetAsync("/api/outbox")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _fabrica.Como(par.AdminTransportista).GetAsync("/api/outbox")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _fabrica.Como(Actor.ServicioInterno).GetAsync("/api/outbox")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _fabrica.CreateClient().GetAsync("/api/outbox")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _fabrica.Como(Actor.Operador).GetAsync("/api/outbox")).StatusCode);
    }

    [Fact]
    public async Task Solo_el_conductor_o_el_supervisor_del_transportista_de_esa_carga_confirma_la_entrega()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var ruta = await CrearRecursoAsync(par);
        var cuerpo = new { estado = "Entregado", ubicacion = "Popayán" };

        Assert.Equal(HttpStatusCode.Forbidden, (await _fabrica.Como(par.AdminGenerador).PostearAsync($"{ruta}/seguimientos", cuerpo)).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await _fabrica.Como(par.SupervisorGenerador).PostearAsync($"{ruta}/seguimientos", cuerpo)).Estado);
        // Un conductor de OTRO transportista tiene el rol correcto pero no es el de esta carga.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _fabrica.Como(new Persona(Guid.NewGuid(), Rol.Conductor)).PostearAsync($"{ruta}/seguimientos", cuerpo)).Estado);
    }

    [Fact]
    public async Task Solo_el_administrador_o_el_supervisor_del_generador_pide_una_carga()
    {
        var par = ParDeOrganizaciones.Nuevo();
        object Carga(Guid generador) => new
        {
            generadorTenantId = generador, transportistaTenantId = par.TransportistaId,
            descripcion = "Residuos industriales no peligrosos", origen = "Cali", destino = "Popayán", pesoKg = 100
        };

        Assert.Equal(HttpStatusCode.Forbidden,
            (await _fabrica.Como(new Persona(par.GeneradorId, Rol.Conductor)).PostearAsync("/api/cargas", Carga(par.GeneradorId))).Estado);
        // Pedir una carga a nombre de otro generador.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _fabrica.Como(par.AdminTransportista).PostearAsync("/api/cargas", Carga(par.GeneradorId))).Estado);
        Assert.Equal(HttpStatusCode.Created,
            (await _fabrica.Como(par.SupervisorGenerador).PostearAsync("/api/cargas", Carga(par.GeneradorId))).Estado);
    }
}

public sealed class AtaquesABillingTests(ApiFactory<BillingApiMarker> fabrica)
    : AtaquesAUnResourceServer<BillingApiMarker>(fabrica), IClassFixture<ApiFactory<BillingApiMarker>>
{
    private readonly ApiFactory<BillingApiMarker> _fabrica = fabrica;

    protected override string RutaDeLectura => "/api/pagos";

    protected override string AudienciaAjena => SeguridadDePrueba.AudCargo;

    protected override (HttpMethod, string, object?)? RutaInterna =>
        (HttpMethod.Post, "/api/eventos/entrega-confirmada", new
        {
            eventId = Guid.NewGuid(), cargaId = Guid.NewGuid(), vehiculoId = Guid.NewGuid(), conductorId = Guid.NewGuid()
        });

    protected override async Task<string> CrearRecursoAsync(ParDeOrganizaciones par)
    {
        var pago = await _fabrica.Como(par.AdminGenerador).PostearAsync("/api/pagos", new
        {
            generadorTenantId = par.GeneradorId, transportistaTenantId = par.TransportistaId, cargaId = Guid.NewGuid(), monto = 850_000m
        });
        Assert.Equal(HttpStatusCode.Created, pago.Estado);
        return $"/api/pagos/{pago.Propiedad("pago").GetProperty("pagoId").GetGuid()}";
    }

    protected override Task<Respuesta> OperacionConRolSinPermisoAsync(ParDeOrganizaciones par, string ruta) =>
        // Mover dinero es del administrador de una organización del pago: un supervisor no lo hace.
        _fabrica.Como(par.SupervisorGenerador).PostearAsync($"{ruta}/liberar");

    [Fact]
    public async Task Liberar_y_reembolsar_son_solo_del_administrador_de_una_organizacion_del_pago()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var ruta = await CrearRecursoAsync(par);

        foreach (var accion in new[] { "liberar", "reembolso" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await _fabrica.Como(par.SupervisorGenerador).PostearAsync($"{ruta}/{accion}")).Estado);
            Assert.Equal(HttpStatusCode.Forbidden, (await _fabrica.Como(par.ConductorTransportista).PostearAsync($"{ruta}/{accion}")).Estado);
            // Un administrador de otra organización tiene el rol, pero no participa en este pago.
            Assert.Equal(HttpStatusCode.Forbidden,
                (await _fabrica.Como(new Persona(Guid.NewGuid(), Rol.Administrador)).PostearAsync($"{ruta}/{accion}")).Estado);
            // Ni siquiera el operador de la plataforma mueve el dinero de un pago ajeno.
            Assert.Equal(HttpStatusCode.Forbidden, (await _fabrica.Como(Actor.Operador).PostearAsync($"{ruta}/{accion}")).Estado);
        }

        Assert.Equal(HttpStatusCode.OK, (await _fabrica.Como(par.AdminTransportista).PostearAsync($"{ruta}/liberar")).Estado);
    }
}
