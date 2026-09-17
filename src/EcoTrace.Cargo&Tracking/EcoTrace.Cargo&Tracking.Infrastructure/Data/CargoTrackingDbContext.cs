using EcoTrace.Cargo_Tracking.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Cargo_Tracking.Infrastructure.Data;

public class CargoTrackingDbContext : DbContext
{
    public CargoTrackingDbContext(DbContextOptions<CargoTrackingDbContext> options) : base(options)
    {
    }

    public DbSet<Cargo> Cargoes => Set<Cargo>();
    public DbSet<CargoAssignment> CargoAssignments => Set<CargoAssignment>();
    public DbSet<TrackingSession> TrackingSessions => Set<TrackingSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Cargo>(entity =>
        {
            entity.ToTable("cargoes");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Description).HasMaxLength(250).IsRequired();
            entity.Property(c => c.OriginAddress).HasMaxLength(200).IsRequired();
            entity.Property(c => c.DestinationAddress).HasMaxLength(200).IsRequired();
            entity.Property(c => c.WeightKg).HasPrecision(18, 2);
            entity.Property(c => c.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(c => c.IsDeleted).HasDefaultValue(false);
            entity.HasQueryFilter(c => !c.IsDeleted);
        });

        modelBuilder.Entity<CargoAssignment>(entity =>
        {
            entity.ToTable("cargo_assignments");
            entity.HasKey(ca => ca.Id);
            entity.Property(ca => ca.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(ca => ca.Notes).HasMaxLength(500);
            entity.Property(ca => ca.IsDeleted).HasDefaultValue(false);
            entity.HasQueryFilter(ca => !ca.IsDeleted);
        });

        modelBuilder.Entity<TrackingSession>(entity =>
        {
            entity.ToTable("tracking_sessions");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(t => t.LastCheckpoint).HasMaxLength(200);
            entity.Property(t => t.IsDeleted).HasDefaultValue(false);
            entity.HasQueryFilter(t => !t.IsDeleted);
        });
    }
}