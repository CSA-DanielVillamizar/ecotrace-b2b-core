using EcoTrace.FleetManagement.Domain;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Infrastructure;

/// <summary>
/// Base de datos propia de Fleet Management (fleet.db). No conoce identity.db ni ninguna otra:
/// TenantId y UserId son columnas planas, sin relacion de EF Core.
/// </summary>
public sealed class FleetManagementDbContext(DbContextOptions<FleetManagementDbContext> options) : DbContext(options)
{
    public DbSet<Conductor> Conductores => Set<Conductor>();

    public DbSet<Vehiculo> Vehiculos => Set<Vehiculo>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FleetManagementDbContext).Assembly);
    }
}
