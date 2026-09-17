using EcoTrace.FleetManagement.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.FleetManagement.Infrastructure.Data;

public class FleetDbContext : DbContext
{
    public FleetDbContext(DbContextOptions<FleetDbContext> options) : base(options)
    {
    }

    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Driver>(entity =>
        {
            entity.ToTable("drivers");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.FullName).HasMaxLength(150).IsRequired();
            entity.Property(d => d.LicenseNumber).HasMaxLength(50).IsRequired();
            entity.HasIndex(d => d.LicenseNumber).IsUnique();
            entity.Property(d => d.Phone).HasMaxLength(30);
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(d => d.IsDeleted).HasDefaultValue(false);
            entity.HasQueryFilter(d => !d.IsDeleted);
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.ToTable("vehicles");
            entity.HasKey(v => v.Id);
            entity.Property(v => v.PlateNumber).HasMaxLength(20).IsRequired();
            entity.HasIndex(v => v.PlateNumber).IsUnique();
            entity.Property(v => v.Brand).HasMaxLength(60).IsRequired();
            entity.Property(v => v.Model).HasMaxLength(60).IsRequired();
            entity.Property(v => v.CapacityKg).HasPrecision(18, 2);
            entity.Property(v => v.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(v => v.IsDeleted).HasDefaultValue(false);
            entity.HasQueryFilter(v => !v.IsDeleted);
        });
    }
}