using EcoTrace.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Identity.Infrastructure;

/// <summary>
/// Base de datos propia de Identity. Ningun otro contexto la conoce ni la consulta:
/// los demas modulos solo guardan TenantId / UserId como valores planos.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RoleClaim> RoleClaims => Set<RoleClaim>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
