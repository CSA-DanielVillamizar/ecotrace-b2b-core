using System.Net;
using EcoTrace.Tests.TestSupport;

namespace EcoTrace.Tests;

public sealed class CargoTrackingApiTests(ApiFactory<CargoTrackingApiMarker> fabrica)
    : IClassFixture<ApiFactory<CargoTrackingApiMarker>>
{
    private readonly HttpClient _cliente = fabrica.CreateClient();

    private static object NuevaCarga(Guid generador, Guid transportista) => new
    {
        generadorTenantId = generador,
        transportistaTenantId = transportista,
        descripcion = "Residuos industriales no peligrosos",
        origen = "Cali",
        destino = "Popayán",
        pesoKg = 4200
    };

    private async Task<Guid> CrearCargaAsync(Guid? generador = null, Guid? transportista = null)
    {
        var carga = await _cliente.PostearAsync("/api/cargas", NuevaCarga(generador ?? Guid.NewGuid(), transportista ?? Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, carga.Estado);
        return carga.Id("cargaId");
    }

    private Task<Respuesta> AsignarAsync(Guid cargaId) =>
        _cliente.PostearAsync($"/api/cargas/{cargaId}/asignacion", new { vehiculoId = Guid.NewGuid(), conductorId = Guid.NewGuid() });

    private Task<Respuesta> SeguimientoAsync(Guid cargaId, string estado, string? nota = null) =>
        _cliente.PostearAsync($"/api/cargas/{cargaId}/seguimientos", new { estado, ubicacion = "Ruta Panamericana km 42", nota });

    [Fact]
    public async Task Una_carga_nueva_queda_pendiente_con_sus_dos_organizaciones()
    {
        var generador = Guid.NewGuid();
        var transportista = Guid.NewGuid();

        var carga = await _cliente.PostearAsync("/api/cargas", NuevaCarga(generador, transportista));

        Assert.Equal(HttpStatusCode.Created, carga.Estado);
        Assert.Equal("Pendiente", carga.Texto("estado"));
        Assert.Equal(generador, carga.Id("generadorTenantId"));
        Assert.Equal(transportista, carga.Id("transportistaTenantId"));
    }

    [Fact]
    public async Task Generador_y_transportista_no_pueden_ser_la_misma_organizacion()
    {
        var mismo = Guid.NewGuid();

        var respuesta = await _cliente.PostearAsync("/api/cargas", NuevaCarga(mismo, mismo));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.Estado);
        Assert.Contains("distintas", respuesta.Texto("detail"));
    }

    [Fact]
    public async Task Recorre_el_ciclo_completo_y_deja_una_linea_de_tiempo_ordenada()
    {
        var cargaId = await CrearCargaAsync();

        Assert.Equal(HttpStatusCode.Created, (await AsignarAsync(cargaId)).Estado);
        Assert.Equal(HttpStatusCode.Created, (await SeguimientoAsync(cargaId, "EnTransito")).Estado);
        Assert.Equal(HttpStatusCode.Created,
            (await SeguimientoAsync(cargaId, "ConNovedad", "Bloqueo en la vía por derrumbe")).Estado);
        Assert.Equal(HttpStatusCode.Created, (await SeguimientoAsync(cargaId, "EnTransito")).Estado);
        var entrega = await SeguimientoAsync(cargaId, "Entregado");

        Assert.Equal(HttpStatusCode.Created, entrega.Estado);
        Assert.Equal("Entregado", entrega.Propiedad("carga").GetProperty("estado").GetString());

        var linea = (await _cliente.ConsultarAsync($"/api/cargas/{cargaId}/seguimientos")).Json
            .EnumerateArray().Select(s => s.GetProperty("estado").GetString()!).ToArray();
        Assert.Equal(new[] { "Asignado", "EnTransito", "ConNovedad", "EnTransito", "Entregado" }, linea);
    }

    [Fact]
    public async Task No_permite_saltarse_estados()
    {
        var cargaId = await CrearCargaAsync();
        await AsignarAsync(cargaId);

        var salto = await SeguimientoAsync(cargaId, "Entregado");

        Assert.Equal(HttpStatusCode.Conflict, salto.Estado);
        Assert.Contains("Asignado a Entregado", salto.Texto("detail"));
    }

    [Fact]
    public async Task No_admite_seguimiento_antes_de_asignar()
    {
        var cargaId = await CrearCargaAsync();

        var respuesta = await SeguimientoAsync(cargaId, "EnTransito");

        Assert.Equal(HttpStatusCode.Conflict, respuesta.Estado);
    }

    [Fact]
    public async Task Una_novedad_exige_nota()
    {
        var cargaId = await CrearCargaAsync();
        await AsignarAsync(cargaId);
        await SeguimientoAsync(cargaId, "EnTransito");

        var sinNota = await SeguimientoAsync(cargaId, "ConNovedad");

        Assert.Equal(HttpStatusCode.BadRequest, sinNota.Estado);
    }

    [Fact]
    public async Task Una_carga_solo_se_asigna_una_vez()
    {
        var cargaId = await CrearCargaAsync();

        var primera = await AsignarAsync(cargaId);
        var segunda = await AsignarAsync(cargaId);

        Assert.Equal(HttpStatusCode.Created, primera.Estado);
        Assert.Equal(HttpStatusCode.Conflict, segunda.Estado);
    }

    [Fact]
    public async Task Guarda_vehiculo_y_conductor_como_identificadores_de_fleet()
    {
        var cargaId = await CrearCargaAsync();
        var vehiculoId = Guid.NewGuid();
        var conductorId = Guid.NewGuid();

        var asignada = await _cliente.PostearAsync($"/api/cargas/{cargaId}/asignacion", new { vehiculoId, conductorId });

        Assert.Equal(vehiculoId, asignada.Propiedad("asignacion").GetProperty("vehiculoId").GetGuid());
        Assert.Equal(conductorId, asignada.Propiedad("asignacion").GetProperty("conductorId").GetGuid());
    }

    [Fact]
    public async Task El_filtro_por_organizacion_coincide_con_cualquiera_de_los_dos_lados()
    {
        var generador = Guid.NewGuid();
        var transportista = Guid.NewGuid();
        await CrearCargaAsync(generador, transportista);
        await CrearCargaAsync();

        var comoGenerador = await _cliente.ConsultarAsync($"/api/cargas?tenantId={generador}");
        var comoTransportista = await _cliente.ConsultarAsync($"/api/cargas?tenantId={transportista}");

        Assert.Equal(1, comoGenerador.Cantidad);
        Assert.Equal(1, comoTransportista.Cantidad);
    }

    [Fact]
    public async Task El_endpoint_de_contexto_declara_referencias_a_identity_y_a_fleet()
    {
        var contexto = await _cliente.ConsultarAsync("/api/_meta/contexto");
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
