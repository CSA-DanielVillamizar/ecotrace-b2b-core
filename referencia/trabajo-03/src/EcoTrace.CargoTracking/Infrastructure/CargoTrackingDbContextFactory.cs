using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EcoTrace.CargoTracking.Infrastructure;

/// <summary>Solo lo usa `dotnet ef` para generar migraciones sin arrancar la API.</summary>
public sealed class CargoTrackingDbContextFactory : IDesignTimeDbContextFactory<CargoTrackingDbContext>
{
    public CargoTrackingDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<CargoTrackingDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new CargoTrackingDbContext(opciones);
    }
}
