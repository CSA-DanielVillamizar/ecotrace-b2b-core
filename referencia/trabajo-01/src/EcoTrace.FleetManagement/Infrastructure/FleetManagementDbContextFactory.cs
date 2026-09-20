using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EcoTrace.FleetManagement.Infrastructure;

/// <summary>Solo lo usa `dotnet ef` para generar migraciones sin arrancar la API.</summary>
public sealed class FleetManagementDbContextFactory : IDesignTimeDbContextFactory<FleetManagementDbContext>
{
    public FleetManagementDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<FleetManagementDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new FleetManagementDbContext(opciones);
    }
}
