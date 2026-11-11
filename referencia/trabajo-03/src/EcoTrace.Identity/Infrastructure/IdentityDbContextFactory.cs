using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EcoTrace.Identity.Infrastructure;

/// <summary>
/// Solo lo usa `dotnet ef` para generar migraciones sin arrancar la API.
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;

        return new IdentityDbContext(opciones);
    }
}
