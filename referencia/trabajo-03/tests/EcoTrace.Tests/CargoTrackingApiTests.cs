using System.Net;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

/// <summary>
/// El módulo de Cargo &amp; Tracking visto solo, sin los otros tres. Cada llamada la hace una persona con el rol y la
/// organización que le corresponden: el administrador del generador pide la carga, el supervisor del transportista
/// la asigna y la sigue, y el operador de la plataforma solo lee. Las reglas de seguridad tienen sus propias pruebas.
/// </summary>
public sealed class CargoTrackingApiTests(ApiFactory<CargoTrackingApiMarker> fabrica)
    : IClassFixture<ApiFactory<CargoTrackingApiMarker>>
{
    private readonly HttpClient _lector = fabrica.ComoOperador();

    private static object NuevaCarga(Guid generador, Guid transportista) => new
    {
        generadorTenantId = generador,
        transportistaTenantId = transportista,
        descripcion = "Residuos industriales no peligrosos",
        origen = "Cali",
        destino = "Popayán",
        pesoKg = 4200
    };

    private async Task<Guid> CrearCargaAsync(ParDeOrganizaciones? par = null)
    {
        par ??= ParDeOrganizaciones.Nuevo();
        var carga = await fabrica.Como(par.AdminGenerador).PostearAsync("/api/cargas", NuevaCarga(par.GeneradorId, par.TransportistaId));
        Assert.Equal(HttpStatusCode.Created, carga.Estado);
        return carga.Id("cargaId");
    }

    private Task<Respuesta> AsignarAsync(ParDeOrganizaciones par, Guid cargaId) =>
        fabrica.Como(par.SupervisorTransportista)
            .PostearAsync($"/api/cargas/{cargaId}/asignacion", new { vehiculoId = Guid.NewGuid(), conductorId = Guid.NewGuid() });

    private Task<Respuesta> SeguimientoAsync(ParDeOrganizaciones par, Guid cargaId, string estado, string? nota = null) =>
        fabrica.Como(par.SupervisorTransportista)
            .PostearAsync($"/api/cargas/{cargaId}/seguimientos", new { estado, ubicacion = "Ruta Panamericana km 42", nota });

    [Fact]
    public async Task Una_carga_nueva_queda_pendiente_con_sus_dos_organizaciones()
    {
        var par = ParDeOrganizaciones.Nuevo();

        var carga = await fabrica.Como(par.AdminGenerador).PostearAsync("/api/cargas", NuevaCarga(par.GeneradorId, par.TransportistaId));

        Assert.Equal(HttpStatusCode.Created, carga.Estado);
        Assert.Equal("Pendiente", carga.Texto("estado"));
        Assert.Equal(par.GeneradorId, carga.Id("generadorTenantId"));
        Assert.Equal(par.TransportistaId, carga.Id("transportistaTenantId"));
    }

    [Fact]
    public async Task Generador_y_transportista_no_pueden_ser_la_misma_organizacion()
    {
        var mismo = Guid.NewGuid();

        var respuesta = await fabrica.Como(new Persona(mismo, Rol.Administrador)).PostearAsync("/api/cargas", NuevaCarga(mismo, mismo));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Contains("distintas", respuesta.Texto("detail"));
    }

    [Fact]
    public async Task Recorre_el_ciclo_completo_y_deja_una_linea_de_tiempo_ordenada()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var cargaId = await CrearCargaAsync(par);

        Assert.Equal(HttpStatusCode.Created, (await AsignarAsync(par, cargaId)).Estado);
        Assert.Equal(HttpStatusCode.Created, (await SeguimientoAsync(par, cargaId, "EnTransito")).Estado);
        Assert.Equal(HttpStatusCode.Created,
            (await SeguimientoAsync(par, cargaId, "ConNovedad", "Bloqueo en la vía por derrumbe")).Estado);
        Assert.Equal(HttpStatusCode.Created, (await SeguimientoAsync(par, cargaId, "EnTransito")).Estado);

        // La entrega la confirma el conductor.
        var entrega = await fabrica.Como(par.ConductorTransportista)
            .PostearAsync($"/api/cargas/{cargaId}/seguimientos", new { estado = "Entregado", ubicacion = "Popayán" });

        Assert.Equal(HttpStatusCode.Created, entrega.Estado);
        Assert.Equal("Entregado", entrega.Propiedad("carga").GetProperty("estado").GetString());

        var linea = (await _lector.ConsultarAsync($"/api/cargas/{cargaId}/seguimientos")).Json
            .EnumerateArray().Select(s => s.GetProperty("estado").GetString()!).ToArray();
        Assert.Equal(new[] { "Asignado", "EnTransito", "ConNovedad", "EnTransito", "Entregado" }, linea);
    }

    [Fact]
    public async Task No_permite_saltarse_estados()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var cargaId = await CrearCargaAsync(par);
        await AsignarAsync(par, cargaId);

        var salto = await SeguimientoAsync(par, cargaId, "Entregado");

        Assert.Equal(HttpStatusCode.Conflict, salto.Estado);
        Assert.Contains("Asignado a Entregado", salto.Texto("detail"));
    }

    [Fact]
    public async Task No_admite_seguimiento_antes_de_asignar()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var cargaId = await CrearCargaAsync(par);

        var respuesta = await SeguimientoAsync(par, cargaId, "EnTransito");

        Assert.Equal(HttpStatusCode.Conflict, respuesta.Estado);
    }

    [Fact]
    public async Task Una_novedad_exige_nota()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var cargaId = await CrearCargaAsync(par);
        await AsignarAsync(par, cargaId);
        await SeguimientoAsync(par, cargaId, "EnTransito");

        var sinNota = await SeguimientoAsync(par, cargaId, "ConNovedad");

        Assert.Equal(HttpStatusCode.BadRequest, sinNota.Estado);
    }

    [Fact]
    public async Task Una_carga_solo_se_asigna_una_vez()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var cargaId = await CrearCargaAsync(par);

        var primera = await AsignarAsync(par, cargaId);
        var segunda = await AsignarAsync(par, cargaId);

        Assert.Equal(HttpStatusCode.Created, primera.Estado);
        Assert.Equal(HttpStatusCode.Conflict, segunda.Estado);
    }

    [Fact]
    public async Task Guarda_vehiculo_y_conductor_como_identificadores_de_fleet()
    {
        var par = ParDeOrganizaciones.Nuevo();
        var cargaId = await CrearCargaAsync(par);
        var vehiculoId = Guid.NewGuid();
        var conductorId = Guid.NewGuid();

        var asignada = await fabrica.Como(par.SupervisorTransportista)
            .PostearAsync($"/api/cargas/{cargaId}/asignacion", new { vehiculoId, conductorId });

        Assert.Equal(vehiculoId, asignada.Propiedad("asignacion").GetProperty("vehiculoId").GetGuid());
        Assert.Equal(conductorId, asignada.Propiedad("asignacion").GetProperty("conductorId").GetGuid());
    }

    [Fact]
    public async Task El_filtro_por_organizacion_coincide_con_cualquiera_de_los_dos_lados()
    {
        var par = ParDeOrganizaciones.Nuevo();
        await CrearCargaAsync(par);
        await CrearCargaAsync();

        var comoGenerador = await _lector.ConsultarAsync($"/api/cargas?tenantId={par.GeneradorId}");
        var comoTransportista = await _lector.ConsultarAsync($"/api/cargas?tenantId={par.TransportistaId}");

        Assert.Equal(1, comoGenerador.Cantidad);
        Assert.Equal(1, comoTransportista.Cantidad);
    }

    [Fact]
    public async Task El_endpoint_de_contexto_declara_referencias_a_identity_y_a_fleet()
    {
        var contexto = await _lector.ConsultarAsync("/api/_meta/contexto");
        var entidades = contexto.Propiedad("entidades").EnumerateArray().ToArray();

        string[] ReferenciasA(string entidad, string destino) => entidades
            .Single(e => e.GetProperty("nombre").GetString() == entidad)
            .GetProperty("referenciasExternas").EnumerateArray()
            .Where(r => r.GetProperty("contexto").GetString() == destino)
            .Select(r => r.GetProperty("campo").GetString()!).Order().ToArray();

        Assert.Equal(new[] { "GeneradorTenantId", "TransportistaTenantId" }, ReferenciasA("Carga", "Identity"));
        Assert.Equal(new[] { "ConductorId", "VehiculoId" }, ReferenciasA("AsignacionCarga", "FleetManagement"));
    }
}
