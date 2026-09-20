using EcoTrace.CargoTracking.Domain;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.CargoTracking.Infrastructure;

/// <summary>
/// Base de datos propia de Cargo &amp; Tracking (cargotracking.db). Las claves foraneas de este
/// contexto solo unen entidades propias (Carga, Asignacion, Seguimiento); los identificadores
/// de Identity y Fleet Management son columnas planas.
/// </summary>
public sealed class CargoTrackingDbContext(DbContextOptions<CargoTrackingDbContext> options) : DbContext(options)
{
    public DbSet<Carga> Cargas => Set<Carga>();

    public DbSet<AsignacionCarga> Asignaciones => Set<AsignacionCarga>();

    public DbSet<Seguimiento> Seguimientos => Set<Seguimiento>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CargoTrackingDbContext).Assembly);
    }
}
