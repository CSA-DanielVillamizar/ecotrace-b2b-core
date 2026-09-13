using EcoTrace.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Identity.Infrastructure;

// Única puerta de acceso a la base de datos del Bounded Context Identity (ADR 0001: sin acceso cruzado).
public class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.Property(t => t.TenantType).HasConversion<string>();
        });

        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Role).HasConversion<string>();
            user.HasIndex(u => u.Email).IsUnique();
            user.HasOne(u => u.Tenant)
                .WithMany(t => t.Users)
                .HasForeignKey(u => u.TenantId);
        });
    }
}
