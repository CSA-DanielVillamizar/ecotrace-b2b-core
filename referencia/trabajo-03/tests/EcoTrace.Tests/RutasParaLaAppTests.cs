using System.Net;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

/// <summary>
/// Lo que la app móvil necesita de los servicios (ADR 0005): cada acción que encola lleva una <c>Idempotency-Key</c> (el
/// <c>OperationId</c> que nació en el dispositivo) y el servidor responde <b>200 con el resultado anterior</b> sin repetir el efecto;
/// y cada rechazo dice de qué tipo es (<c>tipoConflicto</c> A, B o C) para que la app sepa si corregir, aceptar la decisión
/// del servidor o pedir revisión.
/// </summary>
public sealed class IdempotenciaDeCargoTests(ApiFactory<CargoTrackingApiMarker> fabrica) : IClassFixture<ApiFactory<CargoTrackingApiMarker>>
{
    private async Task<(ParDeOrganizaciones Par, Guid CargaId, Guid ConductorId)> CargaEnTransitoAsync()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var carga = await fabrica.Como(par.AdminGenerador).PostearAsync("/api/cargas", new
        {
            generadorTenantId = par.GeneradorId, transportistaTenantId = par.TransportistaId,
            descripcion = "Residuos industriales no peligrosos", origen = "Cali", destino = "Popayán", pesoKg = 4200
        });
        var cargaId = carga.Id("cargaId");
        var conductorId = Guid.NewGuid();
        var supervisor = fabrica.Como(par.SupervisorTransportista);
        await supervisor.PostearAsync($"/api/cargas/{cargaId}/asignacion", new { vehiculoId = Guid.NewGuid(), conductorId });
        await supervisor.PostearAsync($"/api/cargas/{cargaId}/seguimientos", new { estado = "EnTransito", ubicacion = "Santander" });
        return (par, cargaId, conductorId);
    }

    private Task<Respuesta> EntregarAsync(ParDeOrganizaciones par, Guid cargaId, string? clave, Guid? conductorId = null, string ubicacion = "Popayán") =>
        fabrica.Como(par.ConductorTransportista).EnviarConClaveAsync(
            HttpMethod.Post, $"/api/cargas/{cargaId}/seguimientos", new { estado = "Entregado", ubicacion, conductorId }, clave);

    private async Task<int> MensajesDelOutboxAsync() => (await fabrica.ComoOperador().ConsultarAsync("/api/outbox")).Cantidad;

    [Fact]
    public async Task Repetir_la_misma_clave_responde_200_con_el_resultado_anterior_y_no_repite_el_efecto()
    {
        var (par, cargaId, _) = await CargaEnTransitoAsync();
        var clave = Guid.NewGuid().ToString();
        var mensajesAntes = await MensajesDelOutboxAsync();

        var primera = await EntregarAsync(par, cargaId, clave);
        var repetida = await EntregarAsync(par, cargaId, clave);

        Assert.Equal(HttpStatusCode.Created, primera.Estado);
        Assert.False(primera.Repetida);
        Assert.Equal(HttpStatusCode.OK, repetida.Estado);
        Assert.True(repetida.Repetida);
        // Mismo contenido (el texto puede diferir en cómo se escapan los acentos).
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(
            System.Text.Json.Nodes.JsonNode.Parse(primera.Json.GetRawText()), System.Text.Json.Nodes.JsonNode.Parse(repetida.Json.GetRawText())));

        // El efecto ocurrió una sola vez: un solo mensaje nuevo para el Saga, no dos.
        Assert.Equal(mensajesAntes + 1, await MensajesDelOutboxAsync());
    }

    [Fact]
    public async Task Sin_la_clave_la_ruta_funciona_como_siempre_y_repetir_es_un_conflicto()
    {
        var (par, cargaId, _) = await CargaEnTransitoAsync();

        var primera = await EntregarAsync(par, cargaId, clave: null);
        var segunda = await EntregarAsync(par, cargaId, clave: null);

        Assert.Equal(HttpStatusCode.Created, primera.Estado);
        Assert.Equal(HttpStatusCode.Conflict, segunda.Estado);
    }

    [Fact]
    public async Task Cinco_reintentos_a_la_vez_con_la_misma_clave_crean_una_sola_entrega()
    {
        var (par, cargaId, _) = await CargaEnTransitoAsync();
        var clave = Guid.NewGuid().ToString();
        var mensajesAntes = await MensajesDelOutboxAsync();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => EntregarAsync(par, cargaId, clave)));

        var resumen = string.Join(", ", respuestas.Select(r => $"{(int)r.Estado}{(r.Repetida ? "r" : string.Empty)}"));
        Assert.True(respuestas.Count(r => r.Estado == HttpStatusCode.Created) == 1, $"Estados: {resumen}");
        Assert.Equal(4, respuestas.Count(r => r.Estado == HttpStatusCode.OK && r.Repetida));
        Assert.Equal(mensajesAntes + 1, await MensajesDelOutboxAsync());
    }

    [Fact]
    public async Task La_misma_clave_con_otro_contenido_no_es_un_reintento_y_se_rechaza()
    {
        var (par, cargaId, _) = await CargaEnTransitoAsync();
        var clave = Guid.NewGuid().ToString();
        await EntregarAsync(par, cargaId, clave, ubicacion: "Popayán");

        var otraCosa = await EntregarAsync(par, cargaId, clave, ubicacion: "Cali");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, otraCosa.Estado);
        Assert.Contains("clave", otraCosa.Texto("detail"));
    }

    [Fact]
    public async Task La_clave_es_de_quien_la_manda_y_de_esa_ruta()
    {
        var (par, cargaId, _) = await CargaEnTransitoAsync();
        var (par2, cargaId2, _) = await CargaEnTransitoAsync();
        var clave = Guid.NewGuid().ToString();

        // La misma clave en otra carga y de otra persona es otra operación: no se confunde con la primera.
        var una = await EntregarAsync(par, cargaId, clave);
        var otra = await EntregarAsync(par2, cargaId2, clave);

        Assert.Equal(HttpStatusCode.Created, una.Estado);
        Assert.Equal(HttpStatusCode.Created, otra.Estado);
        Assert.False(otra.Repetida);
    }

    [Fact]
    public async Task Un_rechazo_no_deja_huella_y_reintentar_con_la_misma_clave_vuelve_a_evaluarlo()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var carga = await fabrica.Como(par.AdminGenerador).PostearAsync("/api/cargas", new
        {
            generadorTenantId = par.GeneradorId, transportistaTenantId = par.TransportistaId,
            descripcion = "Residuos industriales no peligrosos", origen = "Cali", destino = "Popayán", pesoKg = 100
        });
        var cargaId = carga.Id("cargaId");
        var clave = Guid.NewGuid().ToString();

        // Todavía sin asignar: no se puede entregar.
        var rechazada = await EntregarAsync(par, cargaId, clave);
        Assert.Equal(HttpStatusCode.Conflict, rechazada.Estado);

        var supervisor = fabrica.Como(par.SupervisorTransportista);
        await supervisor.PostearAsync($"/api/cargas/{cargaId}/asignacion", new { vehiculoId = Guid.NewGuid(), conductorId = Guid.NewGuid() });
        await supervisor.PostearAsync($"/api/cargas/{cargaId}/seguimientos", new { estado = "EnTransito", ubicacion = "Santander" });

        var reintento = await EntregarAsync(par, cargaId, clave);

        Assert.Equal(HttpStatusCode.Created, reintento.Estado);
        Assert.False(reintento.Repetida);
    }

    [Fact]
    public async Task Una_clave_inventada_o_demasiado_larga_se_rechaza_como_error_de_tipo_A()
    {
        var (par, cargaId, _) = await CargaEnTransitoAsync();

        var larga = await EntregarAsync(par, cargaId, new string('k', 101));

        Assert.Equal(HttpStatusCode.BadRequest, larga.Estado);
        Assert.Equal("A", larga.TipoConflicto);
    }

    // ---------------------------------------------------------------- conflictos tipificados

    [Fact]
    public async Task Si_la_carga_se_reasigno_mientras_no_habia_senal_el_servidor_responde_409_de_tipo_B()
    {
        var (par, cargaId, conductorAsignado) = await CargaEnTransitoAsync();

        var reasignada = await EntregarAsync(par, cargaId, Guid.NewGuid().ToString(), conductorId: Guid.NewGuid());
        var delConductorCorrecto = await EntregarAsync(par, cargaId, Guid.NewGuid().ToString(), conductorId: conductorAsignado);

        Assert.Equal(HttpStatusCode.Conflict, reasignada.Estado);
        Assert.Equal("B", reasignada.TipoConflicto);
        Assert.Contains("reasignada", reasignada.Texto("detail"));
        Assert.Equal(HttpStatusCode.Created, delConductorCorrecto.Estado);
    }

    [Fact]
    public async Task Los_rechazos_declaran_su_tipo_A_por_dato_invalido_y_B_por_estado()
    {
        var (par, cargaId, _) = await CargaEnTransitoAsync();
        var supervisor = fabrica.Como(par.SupervisorTransportista);

        var sinNota = await supervisor.PostearAsync($"/api/cargas/{cargaId}/seguimientos", new { estado = "ConNovedad", ubicacion = "Ruta" });
        var campoFaltante = await supervisor.PostearAsync($"/api/cargas/{cargaId}/seguimientos", new { ubicacion = "Ruta" });
        var saltoDeEstado = await supervisor.PostearAsync($"/api/cargas/{cargaId}/seguimientos", new { estado = "Pendiente", ubicacion = "Ruta" });

        Assert.Equal("A", sinNota.TipoConflicto);
        Assert.Equal("A", campoFaltante.TipoConflicto);
        Assert.Equal("B", saltoDeEstado.TipoConflicto);
    }
}

public sealed class NovedadesDeFleetTests(ApiFactory<FleetManagementApiMarker> fabrica) : IClassFixture<ApiFactory<FleetManagementApiMarker>>
{
    private async Task<(Persona Supervisor, Persona Conductor, Guid VehiculoId, Guid ConductorId)> FlotaAsync()
    {
        var tenant = Guid.NewGuid();
        var supervisor = new Persona(tenant, Rol.Supervisor);
        var conductorPersona = new Persona(tenant, Rol.Conductor);
        var vehiculo = await fabrica.Como(supervisor).PostearAsync("/api/vehiculos", new
        {
            tenantId = tenant, registradoPorUserId = supervisor.UserId,
            placa = "N" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(), capacidadKg = 5000
        });
        var conductor = await fabrica.Como(supervisor).PostearAsync("/api/conductores", new
        {
            tenantId = tenant, registradoPorUserId = supervisor.UserId, nombre = "Carlos Ruiz",
            licencia = "L" + Guid.NewGuid().ToString("N")[..6]
        });
        return (supervisor, conductorPersona, vehiculo.Id("vehiculoId"), conductor.Id("conductorId"));
    }

    private Task<Respuesta> ReportarAsync(Persona quien, Guid vehiculoId, Guid cargaId, string? clave, string descripcion = "Neumático delantero con desgaste") =>
        fabrica.Como(quien).EnviarConClaveAsync(HttpMethod.Post, $"/api/vehiculos/{vehiculoId}/novedades", new { cargaId, descripcion }, clave);

    [Fact]
    public async Task El_conductor_reporta_una_novedad_y_repetirla_con_la_misma_clave_no_la_duplica()
    {
        var (_, conductor, vehiculoId, _) = await FlotaAsync();
        var cargaId = Guid.NewGuid();
        var clave = Guid.NewGuid().ToString();

        var primera = await ReportarAsync(conductor, vehiculoId, cargaId, clave);
        var repetida = await ReportarAsync(conductor, vehiculoId, cargaId, clave);

        Assert.Equal(HttpStatusCode.Created, primera.Estado);
        Assert.Equal(conductor.UserId, primera.Id("reportadaPorUserId"));
        Assert.Equal(HttpStatusCode.OK, repetida.Estado);
        Assert.True(repetida.Repetida);
        Assert.Equal(primera.Id("novedadId"), repetida.Id("novedadId"));
    }

    [Fact]
    public async Task Si_el_vehiculo_se_reservo_para_otra_carga_mientras_no_habia_senal_responde_409_de_tipo_B()
    {
        var (_, conductor, vehiculoId, conductorId) = await FlotaAsync();
        var cargaDeLaNovedad = Guid.NewGuid();
        var otraCarga = Guid.NewGuid();
        var reserva = await fabrica.Como(Actor.ServicioInterno)
            .PostearAsync("/api/reservas", new { cargaId = otraCarga, vehiculoId, conductorId });
        Assert.Equal(HttpStatusCode.Created, reserva.Estado);
        var clave = Guid.NewGuid().ToString();

        var rechazada = await ReportarAsync(conductor, vehiculoId, cargaDeLaNovedad, clave);

        Assert.Equal(HttpStatusCode.Conflict, rechazada.Estado);
        Assert.Equal("B", rechazada.TipoConflicto);

        // Sobre la carga para la que SÍ está reservado, la novedad se acepta.
        Assert.Equal(HttpStatusCode.Created, (await ReportarAsync(conductor, vehiculoId, otraCarga, Guid.NewGuid().ToString())).Estado);

        // Y un rechazo no deja huella: liberado el vehículo, la misma clave vuelve a evaluarse y ahora pasa.
        await fabrica.Como(Actor.ServicioInterno).PostearAsync("/api/liberaciones", new { cargaId = otraCarga, vehiculoId, conductorId });
        Assert.Equal(HttpStatusCode.Created, (await ReportarAsync(conductor, vehiculoId, cargaDeLaNovedad, clave)).Estado);
    }

    [Fact]
    public async Task Una_descripcion_corta_es_un_error_de_tipo_A_que_se_puede_corregir()
    {
        var (_, conductor, vehiculoId, _) = await FlotaAsync();

        var respuesta = await ReportarAsync(conductor, vehiculoId, Guid.NewGuid(), Guid.NewGuid().ToString(), descripcion: "ok");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Equal("A", respuesta.TipoConflicto);
    }

    [Fact]
    public async Task Solo_la_gente_de_la_organizacion_dueña_del_vehiculo_reporta_novedades()
    {
        var (_, _, vehiculoId, _) = await FlotaAsync();
        var ajena = new Persona(Guid.NewGuid(), Rol.Conductor);
        var auditor = new Persona(Guid.NewGuid(), Rol.Auditor);

        Assert.Equal(HttpStatusCode.Forbidden, (await ReportarAsync(ajena, vehiculoId, Guid.NewGuid(), null)).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await ReportarAsync(auditor, vehiculoId, Guid.NewGuid(), null)).Estado);
        // Ni siquiera el operador de la plataforma escribe datos de otra organización.
        Assert.Equal(HttpStatusCode.Forbidden, (await fabrica.Como(Actor.Operador).PostearAsync(
            $"/api/vehiculos/{vehiculoId}/novedades", new { cargaId = Guid.NewGuid(), descripcion = "Novedad del operador" })).Estado);
        Assert.Equal(HttpStatusCode.NotFound, (await ReportarAsync(ajena, Guid.NewGuid(), Guid.NewGuid(), null)).Estado);
    }
}

public sealed class EvidenciasDeBillingTests(ApiFactory<BillingApiMarker> fabrica) : IClassFixture<ApiFactory<BillingApiMarker>>
{
    private async Task<(ParDeOrganizaciones Par, Guid PagoId)> PagoAsync()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var pago = await fabrica.Como(par.AdminGenerador).PostearAsync("/api/pagos", new
        {
            generadorTenantId = par.GeneradorId, transportistaTenantId = par.TransportistaId, cargaId = Guid.NewGuid(), monto = 850_000m
        });
        return (par, pago.Propiedad("pago").GetProperty("pagoId").GetGuid());
    }

    private Task<Respuesta> EvidenciaAsync(Persona quien, Guid pagoId, string? clave, string descripcion = "Acta de entrega firmada por la planta") =>
        fabrica.Como(quien).EnviarConClaveAsync(HttpMethod.Post, $"/api/pagos/{pagoId}/evidencias", new { descripcion, referencia = "foto-0001" }, clave);

    [Fact]
    public async Task El_conductor_registra_la_evidencia_y_queda_en_la_auditoria_del_pago_una_sola_vez()
    {
        var (par, pagoId) = await PagoAsync();
        var clave = Guid.NewGuid().ToString();

        var primera = await EvidenciaAsync(par.ConductorTransportista, pagoId, clave);
        var repetida = await EvidenciaAsync(par.ConductorTransportista, pagoId, clave);

        Assert.Equal(HttpStatusCode.Created, primera.Estado);
        Assert.Equal(HttpStatusCode.OK, repetida.Estado);
        Assert.True(repetida.Repetida);

        var auditoria = await fabrica.ComoOperador().ConsultarAsync($"/api/pagos/{pagoId}/auditoria");
        var acciones = auditoria.Json.EnumerateArray().Select(a => a.GetProperty("accion").GetString()).ToArray();
        Assert.Equal(1, acciones.Count(a => a == "Evidencia de entrega registrada"));
    }

    [Fact]
    public async Task Si_el_pago_ya_fue_liberado_por_otro_proceso_la_evidencia_responde_409_de_tipo_B()
    {
        var (par, pagoId) = await PagoAsync();
        await fabrica.Como(par.AdminGenerador).PostearAsync($"/api/pagos/{pagoId}/liberar");

        var tarde = await EvidenciaAsync(par.ConductorTransportista, pagoId, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Conflict, tarde.Estado);
        Assert.Equal("B", tarde.TipoConflicto);
        Assert.Contains("liberado", tarde.Texto("detail"));
    }

    [Fact]
    public async Task Una_evidencia_sin_descripcion_util_es_un_error_de_tipo_A()
    {
        var (par, pagoId) = await PagoAsync();

        var respuesta = await EvidenciaAsync(par.ConductorTransportista, pagoId, null, descripcion: "x");

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Equal("A", respuesta.TipoConflicto);
    }

    [Fact]
    public async Task Solo_las_organizaciones_del_pago_registran_evidencia()
    {
        var (_, pagoId) = await PagoAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await EvidenciaAsync(new Persona(Guid.NewGuid(), Rol.Conductor), pagoId, null)).Estado);
        Assert.Equal(HttpStatusCode.Forbidden, (await EvidenciaAsync(new Persona(Guid.NewGuid(), Rol.Administrador), pagoId, null)).Estado);
    }
}

public sealed class PerfilDeIdentityTests(ApiFactory<IdentityApiMarker> fabrica) : IClassFixture<ApiFactory<IdentityApiMarker>>
{
    private async Task<(Guid TenantId, Persona Persona)> ConductorAsync()
    {
        var operador = fabrica.ComoOperador();
        var tenant = await operador.PostearAsync("/api/tenants", new { nombre = $"Transportes {Guid.NewGuid():N}"[..20], tenantType = "Transportista" });
        var usuario = await operador.PostearAsync("/api/users", new
        {
            tenantId = tenant.Id("tenantId"), role = "Conductor", nombre = "Carlos Ruiz",
            email = $"carlos.{Guid.NewGuid():N}@ejemplo.co", password = "una-clave-larga-123"
        });
        return (tenant.Id("tenantId"), new Persona(tenant.Id("tenantId"), Rol.Conductor) { UserId = usuario.Id("userId") });
    }

    private Task<Respuesta> CambiarAsync(Persona quien, Guid usuarioId, string telefono, string? clave) =>
        fabrica.Como(quien).EnviarConClaveAsync(HttpMethod.Put, $"/api/users/{usuarioId}/perfil", new { telefono }, clave);

    [Fact]
    public async Task El_conductor_cambia_su_telefono_y_repetir_la_clave_no_cambia_nada_mas()
    {
        var (_, conductor) = await ConductorAsync();
        var clave = Guid.NewGuid().ToString();

        var primera = await CambiarAsync(conductor, conductor.UserId, "+57 300 123 4567", clave);
        var repetida = await CambiarAsync(conductor, conductor.UserId, "+57 300 123 4567", clave);

        Assert.Equal(HttpStatusCode.OK, primera.Estado);
        Assert.Equal("+573001234567", primera.Texto("telefono"));
        Assert.False(primera.Repetida);
        Assert.True(repetida.Repetida);
    }

    [Fact]
    public async Task Un_telefono_con_formato_invalido_es_un_error_de_tipo_A()
    {
        var (_, conductor) = await ConductorAsync();

        var respuesta = await CambiarAsync(conductor, conductor.UserId, "no-es-un-telefono", Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Equal("A", respuesta.TipoConflicto);
    }

    [Fact]
    public async Task Un_telefono_vacio_es_un_error_de_tipo_A_con_la_regla_explicada_en_espanol()
    {
        var (_, conductor) = await ConductorAsync();

        var respuesta = await CambiarAsync(conductor, conductor.UserId, string.Empty, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Equal("A", respuesta.TipoConflicto);
        Assert.Contains("entre 7 y 15 dígitos", respuesta.Texto("detail"));
    }

    [Fact]
    public async Task Un_cuerpo_sin_el_campo_obligatorio_dice_en_espanol_que_campos_revisar()
    {
        var (_, conductor) = await ConductorAsync();

        var respuesta = await fabrica.Como(conductor)
            .EnviarConClaveAsync(HttpMethod.Put, $"/api/users/{conductor.UserId}/perfil", new { }, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Equal("A", respuesta.TipoConflicto);
        Assert.StartsWith("Revisa estos campos:", respuesta.Texto("detail"));
    }

    [Fact]
    public async Task Si_la_organizacion_se_suspendio_mientras_no_habia_senal_responde_409_de_tipo_B()
    {
        var (tenantId, conductor) = await ConductorAsync();
        await fabrica.ComoOperador().PostearAsync($"/api/tenants/{tenantId}/suspension");

        var respuesta = await CambiarAsync(conductor, conductor.UserId, "3001234567", Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Conflict, respuesta.Estado);
        Assert.Equal("B", respuesta.TipoConflicto);
        Assert.Contains("suspendida", respuesta.Texto("detail"));
    }

    [Fact]
    public async Task Cada_persona_cambia_solo_su_propio_perfil()
    {
        var (_, uno) = await ConductorAsync();
        var (_, otro) = await ConductorAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await CambiarAsync(uno, otro.UserId, "3001234567", null)).Estado);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await CambiarAsync(new Persona(uno.TenantId, Rol.Administrador), otro.UserId, "3001234567", null)).Estado);
    }
}
