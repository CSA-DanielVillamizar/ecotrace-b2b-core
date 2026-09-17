using EcoTrace.Identity.Domain.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Identity.Infrastructure.Data;

public class IdentityDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options) : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Tenant>(entity =>
        {
            entity.ToTable("tenants");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).HasMaxLength(150).IsRequired();
            entity.Property(t => t.TaxId).HasMaxLength(50).IsRequired();
            entity.HasIndex(t => t.TaxId).IsUnique();
            entity.Property(t => t.Type).HasConversion<string>().HasMaxLength(30);
            entity.Property(t => t.IsActive).HasDefaultValue(true);
            entity.Property(t => t.IsDeleted).HasDefaultValue(false);
            entity.HasQueryFilter(t => !t.IsDeleted);
        });

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("AspNetUsers");
            entity.Property(u => u.FullName).HasMaxLength(150).IsRequired();
            entity.Property(u => u.IsActive).HasDefaultValue(true);
            entity.Property(u => u.IsDeleted).HasDefaultValue(false);
            entity.HasQueryFilter(u => !u.IsDeleted);
        });

        builder.Entity<ApplicationRole>(entity =>
        {
            entity.ToTable("AspNetRoles");
            entity.Property(r => r.Description).HasMaxLength(250);
        });
    }
}