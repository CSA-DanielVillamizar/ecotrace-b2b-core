using System.Net;
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
