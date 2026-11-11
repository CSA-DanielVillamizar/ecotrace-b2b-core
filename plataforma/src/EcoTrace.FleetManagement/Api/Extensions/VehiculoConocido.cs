using EcoTrace.FleetManagement.Domain;
using EcoTrace.FleetManagement.Infrastructure;

namespace EcoTrace.FleetManagement.Api.Extensions;

/// <summary>
/// Capa 2 del release (ADR 0006): un vehículo con identificador fijo que solo existe en producción. Si la
/// verificación lo encuentra, el backend que respondió es de verdad el de producción; si responde 404, la
/// app o el script apuntan a otro entorno y el release no se aprueba.
/// </summary>
public static class VehiculoConocido
{
    private const string Seccion = "Semilla:VehiculoConocido";

    /// <summary>Lo crea si la configuración lo pide (solo appsettings.Production.json) y aún no existe.</summary>
    public static WebApplication SembrarVehiculoConocido(this WebApplication app)
    {
        var semilla = app.Configuration.GetSection(Seccion);
        if (!semilla.Exists())
        {
            return app;
        }

        var vehiculoId = semilla.GetValue<Guid>("VehiculoId");
        using var alcance = app.Services.CreateScope();
        var db = alcance.ServiceProvider.GetRequiredService<FleetManagementDbContext>();
        if (db.Vehiculos.Any(v => v.VehiculoId == vehiculoId))
        {
            return app;
        }

        var ahora = alcance.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        db.Vehiculos.Add(Vehiculo.Crear(
            semilla.GetValue<Guid>("TenantId"),
            semilla.GetValue<Guid>("RegistradoPorUserId"),
            semilla.GetValue<string>("Placa"),
            semilla.GetValue<int>("CapacidadKg"),
            ahora,
            vehiculoId));
        db.SaveChanges();

        app.Logger.LogInformation("Vehículo conocido de producción sembrado ({VehiculoId}).", vehiculoId);
        return app;
    }
}
