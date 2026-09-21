using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EcoTrace.Billing.Infrastructure;

/// <summary>Solo lo usa `dotnet ef` para generar migraciones sin arrancar la API.</summary>
public sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<BillingDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new BillingDbContext(opciones);
    }
}
