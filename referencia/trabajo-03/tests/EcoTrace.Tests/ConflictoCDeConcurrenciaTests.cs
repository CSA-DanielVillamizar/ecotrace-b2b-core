using System.Net;
using EcoTrace.FleetManagement.Domain;
using EcoTrace.FleetManagement.Infrastructure;
using EcoTrace.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EcoTrace.Tests;

/// <summary>
/// El conflicto de tipo C: otra persona cambió lo mismo mientras tanto (edición concurrente). Una carrera de verdad no se puede
/// repetir a voluntad, así que aquí la base de datos lanza el choque de concurrencia en el momento de guardar. Lo que se comprueba
/// es lo que el servidor hace con él: responde 409 con <c>tipoConflicto</c> «C» (la app lo pide a revisión, no reintenta solo)
/// y el rechazo no deja huella, de modo que la misma <c>Idempotency-Key</c> se acepta cuando el choque ya no ocurre.
/// </summary>
public sealed class ConflictoCDeConcurrenciaTests : IAsyncLifetime
{
    private readonly ApiFactory<FleetManagementApiMarker> _base = new();
    private readonly ChoqueDeConcurrencia _choque = new();
    private WebApplicationFactory<FleetManagementApiMarker> _fleet = null!;

    public Task InitializeAsync()
    {
        _fleet = _base.WithWebHostBuilder(b => b.ConfigureTestServices(servicios =>
        {
            servicios.RemoveAll<DbContextOptions<FleetManagementDbContext>>();
            servicios.AddDbContext<FleetManagementDbContext>((proveedor, opciones) =>
                opciones.UseSqlite(proveedor.GetRequiredService<IConfiguration>().GetConnectionString("Default")).AddInterceptors(_choque));
        }));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _fleet.Dispose();
        await _base.DisposeAsync();
    }

    private HttpClient Como(Actor actor) =>
        _fleet.CreateClient().ConToken(actor.TokenPara(ClientesDePrueba.AudienciaDe<FleetManagementApiMarker>()));

    private Task<Respuesta> ReportarAsync(Persona quien, Guid vehiculoId, string clave) =>
        Como(quien).EnviarConClaveAsync(
            HttpMethod.Post, $"/api/vehiculos/{vehiculoId}/novedades",
            new { cargaId = Guid.NewGuid(), descripcion = "Neumático delantero con desgaste" }, clave);

    [Fact]
    public async Task Un_choque_de_concurrencia_responde_409_de_tipo_C_y_no_deja_huella()
    {
        var tenant = Guid.NewGuid();
        var supervisor = new Persona(tenant, Rol.Supervisor);
        var conductor = new Persona(tenant, Rol.Conductor);
        var vehiculo = await Como(supervisor).PostearAsync("/api/vehiculos", new
        {
            tenantId = tenant, registradoPorUserId = supervisor.UserId,
            placa = "C" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(), capacidadKg = 5000
        });
        var vehiculoId = vehiculo.Id("vehiculoId");
        var clave = Guid.NewGuid().ToString();

        _choque.Activo = true;
        var rechazada = await ReportarAsync(conductor, vehiculoId, clave);

        Assert.Equal(HttpStatusCode.Conflict, rechazada.Estado);
        Assert.Equal("C", rechazada.TipoConflicto);

        // Sin el choque, la misma clave se evalúa de nuevo y pasa: el rechazo no guardó nada.
        _choque.Activo = false;
        var reintento = await ReportarAsync(conductor, vehiculoId, clave);
        Assert.Equal(HttpStatusCode.Created, reintento.Estado);
        Assert.False(reintento.Repetida);
    }

    /// <summary>Hace como si otra solicitud hubiera modificado el mismo registro justo antes de guardar una novedad.</summary>
    private sealed class ChoqueDeConcurrencia : SaveChangesInterceptor
    {
        public volatile bool Activo;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> resultado, CancellationToken ct = default)
        {
            if (Activo && eventData.Context!.ChangeTracker.Entries<NovedadVehiculo>().Any(e => e.State == EntityState.Added))
            {
                throw new DbUpdateConcurrencyException("Otra solicitud cambió el mismo registro.");
            }

            return base.SavingChangesAsync(eventData, resultado, ct);
        }
    }
}
