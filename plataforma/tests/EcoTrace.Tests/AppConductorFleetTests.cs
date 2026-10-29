using System.Net;
using System.Net.Http.Json;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

/// <summary>
/// Las rutas de Fleet que llama la app del conductor (Trabajo 3, ADR 0005). El conductor inicia sesión de
/// verdad en Identity: su token trae su UserId en <c>sub</c> y su organización en <c>tenant_id</c>.
/// </summary>
public sealed class AppConductorFleetTests(FleetConIdentity fleet) : IClassFixture<FleetConIdentity>
{
    /// <summary>Un conductor con cuenta en Identity, registrado en Fleet, y un vehículo de su misma organización.</summary>
    private sealed record Conductor(string Token, Guid TenantId, Guid ConductorId, Guid VehiculoId);

    [Fact]
    public async Task El_conductor_ve_su_registro_y_el_vehiculo_reservado_para_su_misma_carga()
    {
        var conductor = await PrepararConductorAsync();
        var cargaId = Guid.NewGuid();
        await ReservarAsync(cargaId, conductor.VehiculoId, conductor.ConductorId);

        using var app = fleet.ClienteFleet(conductor.Token);
        var asignacion = await app.ConsultarAsync("/api/conductores/yo");

        Assert.Equal(HttpStatusCode.OK, asignacion.Estado);
        Assert.Equal(conductor.ConductorId, asignacion.Propiedad("conductor").GetProperty("conductorId").GetGuid());
        Assert.Equal(conductor.VehiculoId, asignacion.Propiedad("vehiculo").GetProperty("vehiculoId").GetGuid());
        Assert.Equal(cargaId, asignacion.Id("cargaId"));
    }

    [Fact]
    public async Task Sin_carga_asignada_ve_su_registro_pero_ningun_vehiculo()
    {
        var conductor = await PrepararConductorAsync();

        using var app = fleet.ClienteFleet(conductor.Token);
        var asignacion = await app.ConsultarAsync("/api/conductores/yo");

        Assert.Equal(HttpStatusCode.OK, asignacion.Estado);
        Assert.Equal("Disponible", asignacion.Propiedad("conductor").GetProperty("estado").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, asignacion.Propiedad("vehiculo").ValueKind);
    }

    [Fact]
    public async Task Un_usuario_que_no_esta_registrado_como_conductor_recibe_404()
    {
        using var app = fleet.ClienteFleet(FleetConIdentity.Token(Guid.NewGuid(), "Conductor"));

        var asignacion = await app.ConsultarAsync("/api/conductores/yo");

        Assert.Equal(HttpStatusCode.NotFound, asignacion.Estado);
    }

    // ---- Novedades del vehículo -----------------------------------------------------------------

    [Fact]
    public async Task El_conductor_reporta_una_novedad_de_su_vehiculo()
    {
        var conductor = await PrepararConductorAsync();
        var cargaId = Guid.NewGuid();
        var ocurrida = DateTimeOffset.UtcNow.AddHours(-2);

        using var app = fleet.ClienteFleet(conductor.Token);
        var novedad = await ReportarAsync(app, conductor.VehiculoId,
            new { tipo = "Llanta", descripcion = "Llanta trasera pinchada en la vía", ocurridaEn = ocurrida, cargaId });
        var lista = await app.ConsultarAsync($"/api/vehiculos/{conductor.VehiculoId}/novedades");

        Assert.Equal(HttpStatusCode.Created, novedad.Estado);
        Assert.Equal(conductor.TenantId, novedad.Id("tenantId"));
        Assert.Equal(cargaId, novedad.Id("cargaId"));
        Assert.Equal("Llanta", novedad.Texto("tipo"));
        // Guarda la hora del teléfono, no la de llegada: pudo reportarse sin señal horas antes.
        Assert.Equal(ocurrida.UtcDateTime, novedad.Propiedad("ocurridaEn").GetDateTime().ToUniversalTime(), TimeSpan.FromSeconds(1));
        Assert.Equal(1, lista.Cantidad);
    }

    [Fact]
    public async Task Rechaza_una_novedad_con_datos_invalidos()
    {
        var conductor = await PrepararConductorAsync();
        using var app = fleet.ClienteFleet(conductor.Token);
        var vehiculo = conductor.VehiculoId;

        var sinDescripcion = await ReportarAsync(app, vehiculo, new { tipo = "Mecanica", descripcion = "", ocurridaEn = DateTimeOffset.UtcNow });
        var enElFuturo = await ReportarAsync(app, vehiculo,
            new { tipo = "Mecanica", descripcion = "Ruido en el motor", ocurridaEn = DateTimeOffset.UtcNow.AddHours(1) });
        var sinTipo = await ReportarAsync(app, vehiculo, new { descripcion = "Ruido en el motor", ocurridaEn = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.BadRequest, sinDescripcion.Estado);
        Assert.Equal(HttpStatusCode.BadRequest, enElFuturo.Estado);
        Assert.Contains("futuro", enElFuturo.Texto("detail"));
        Assert.Equal(HttpStatusCode.BadRequest, sinTipo.Estado);
    }

    [Fact]
    public async Task No_se_reportan_novedades_de_un_vehiculo_de_otra_organizacion_ni_de_uno_que_no_existe()
    {
        var conductor = await PrepararConductorAsync();
        var otro = await PrepararConductorAsync();
        using var app = fleet.ClienteFleet(conductor.Token);
        var cuerpo = new { tipo = "Otra", descripcion = "Revisión pendiente", ocurridaEn = DateTimeOffset.UtcNow };

        var deOtraOrganizacion = await ReportarAsync(app, otro.VehiculoId, cuerpo);
        var inexistente = await ReportarAsync(app, Guid.NewGuid(), cuerpo);

        Assert.Equal(HttpStatusCode.Forbidden, deOtraOrganizacion.Estado);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.Estado);
    }

    // ---- Idempotencia: un reintento no duplica ---------------------------------------------------

    [Fact]
    public async Task Un_reintento_con_el_mismo_OperationId_no_duplica_la_novedad()
    {
        var conductor = await PrepararConductorAsync();
        using var app = fleet.ClienteFleet(conductor.Token);
        var operationId = Guid.NewGuid();
        var cuerpo = new { tipo = "Mecanica", descripcion = "Se encendió la luz del motor", ocurridaEn = DateTimeOffset.UtcNow };

        // La app envió, se cortó la red antes de recibir la respuesta y reintenta con el mismo OperationId.
        var primera = await ReportarAsync(app, conductor.VehiculoId, cuerpo, operationId);
        var reintento = await ReportarAsync(app, conductor.VehiculoId, cuerpo, operationId);
        var lista = await app.ConsultarAsync($"/api/vehiculos/{conductor.VehiculoId}/novedades");

        Assert.Equal(HttpStatusCode.Created, primera.Estado);
        Assert.Equal(HttpStatusCode.OK, reintento.Estado);
        Assert.Equal(primera.Id("novedadId"), reintento.Id("novedadId"));
        Assert.Equal(1, lista.Cantidad);
    }

    [Fact]
    public async Task Reintentos_simultaneos_con_el_mismo_OperationId_crean_una_sola_novedad()
    {
        var conductor = await PrepararConductorAsync();
        using var app = fleet.ClienteFleet(conductor.Token);
        var operationId = Guid.NewGuid();
        var cuerpo = new { tipo = "Llanta", descripcion = "Llanta delantera baja", ocurridaEn = DateTimeOffset.UtcNow };

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => ReportarAsync(app, conductor.VehiculoId, cuerpo, operationId)));
        var lista = await app.ConsultarAsync($"/api/vehiculos/{conductor.VehiculoId}/novedades");

        Assert.Equal(1, respuestas.Count(r => r.Estado == HttpStatusCode.Created));
        Assert.All(respuestas, r => Assert.True(r.Estado is HttpStatusCode.Created or HttpStatusCode.OK, $"Respondió {r.Estado}"));
        Assert.Single(respuestas.Select(r => r.Id("novedadId")).Distinct());
        Assert.Equal(1, lista.Cantidad);
    }

    [Fact]
    public async Task Dos_acciones_distintas_son_dos_novedades_aunque_digan_lo_mismo()
    {
        var conductor = await PrepararConductorAsync();
        using var app = fleet.ClienteFleet(conductor.Token);
        var cuerpo = new { tipo = "Otra", descripcion = "Revisión de frenos", ocurridaEn = DateTimeOffset.UtcNow };

        await ReportarAsync(app, conductor.VehiculoId, cuerpo);
        await ReportarAsync(app, conductor.VehiculoId, cuerpo);

        Assert.Equal(2, (await app.ConsultarAsync($"/api/vehiculos/{conductor.VehiculoId}/novedades")).Cantidad);
    }

    [Fact]
    public async Task Sin_Idempotency_Key_responde_400()
    {
        var conductor = await PrepararConductorAsync();
        using var app = fleet.ClienteFleet(conductor.Token);

        var respuesta = await app.PostearAsync($"/api/vehiculos/{conductor.VehiculoId}/novedades",
            new { tipo = "Otra", descripcion = "Revisión de frenos", ocurridaEn = DateTimeOffset.UtcNow });

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Contains("Idempotency-Key", respuesta.Texto("detail"));
    }

    [Fact]
    public async Task Una_clave_usada_en_el_vehiculo_de_otra_organizacion_no_devuelve_su_novedad()
    {
        var conductor = await PrepararConductorAsync();
        var otro = await PrepararConductorAsync();
        var operationId = Guid.NewGuid();
        var cuerpo = new { tipo = "Otra", descripcion = "Revisión de frenos", ocurridaEn = DateTimeOffset.UtcNow };

        using var appDelOtro = fleet.ClienteFleet(otro.Token);
        await ReportarAsync(appDelOtro, otro.VehiculoId, cuerpo, operationId);
        using var app = fleet.ClienteFleet(conductor.Token);
        var conLaMismaClave = await ReportarAsync(app, conductor.VehiculoId, cuerpo, operationId);

        Assert.Equal(HttpStatusCode.Conflict, conLaMismaClave.Estado);
        Assert.False(conLaMismaClave.Json.TryGetProperty("novedadId", out _));
    }

    /// <summary>Envía la novedad como el motor de la app: con el OperationId en el encabezado Idempotency-Key.</summary>
    private static async Task<Respuesta> ReportarAsync(HttpClient app, Guid vehiculoId, object cuerpo, Guid? operationId = null)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, $"/api/vehiculos/{vehiculoId}/novedades")
        {
            Content = JsonContent.Create(cuerpo)
        };
        solicitud.Headers.Add("Idempotency-Key", (operationId ?? Guid.NewGuid()).ToString());
        return await Respuesta.De(await app.SendAsync(solicitud));
    }

    /// <summary>
    /// Identity crea la organización y el usuario conductor; la supervisora lo registra en Fleet con su
    /// UserId y registra un vehículo; el conductor inicia sesión.
    /// </summary>
    private async Task<Conductor> PrepararConductorAsync()
    {
        using var admin = await Acceso.ClienteAdministradorAsync(fleet.ClienteIdentity);
        var sufijo = Guid.NewGuid().ToString("N");
        var tenantId = (await admin.PostearAsync("/api/tenants", new { nombre = $"Transportes {sufijo[..6]}", tenantType = "Transportista" })).Id("tenantId");
        var email = $"conductor.{sufijo}@x.co";
        var userId = await admin.CrearUsuarioAsync(tenantId, "Conductor", email, "Conductor-2026");

        using var supervisora = fleet.ClienteFleet(FleetConIdentity.Token(tenantId));
        var conductorId = (await supervisora.PostearAsync("/api/conductores",
            new { userId, nombre = "Carlos Ruiz", licencia = $"C2-{sufijo[..6]}" })).Id("conductorId");
        var vehiculoId = (await supervisora.PostearAsync("/api/vehiculos",
            new { placa = $"C{sufijo[..6].ToUpperInvariant()}", capacidadKg = 5000 })).Id("vehiculoId");

        return new Conductor(await admin.TokenDeAsync(email, "Conductor-2026"), tenantId, conductorId, vehiculoId);
    }

    /// <summary>Lo que hace el Saga de Billing: reservar vehículo y conductor juntos para una carga.</summary>
    private async Task ReservarAsync(Guid cargaId, Guid vehiculoId, Guid conductorId)
    {
        using var servicio = fleet.ClienteFleet(FleetConIdentity.TokenDeServicio());
        var reserva = await servicio.PostearAsync("/api/reservas", new { cargaId, vehiculoId, conductorId });
        Assert.Equal(HttpStatusCode.Created, reserva.Estado);
    }
}
