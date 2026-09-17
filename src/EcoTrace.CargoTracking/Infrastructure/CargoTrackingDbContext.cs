using Microsoft.EntityFrameworkCore;
using EcoTrace.CargoTracking.Domain;

namespace EcoTrace.CargoTracking.Infrastructure;

// Este es el ÚNICO punto del proyecto que "sabe" hablar con PostgreSQL.
// Solo administra las entidades de ESTE módulo. Nunca debe agregarse
// aquí un DbSet<Tenant>, DbSet<Vehiculo>, DbSet<Conductor>, etc.
public class CargoTrackingDbContext : DbContext
{
    public CargoTrackingDbContext(DbContextOptions<CargoTrackingDbContext> options)
        : base(options) { }

    public DbSet<Carga> Cargas => Set<Carga>();
    public DbSet<AsignacionCarga> Asignaciones => Set<AsignacionCarga>();
    public DbSet<Seguimiento> Seguimientos => Set<Seguimiento>();

    // Las 2 relaciones reales del módulo: Carga->AsignacionCarga y
    // Carga->Seguimiento. Ambas válidas porque las 3 entidades
    // pertenecen al mismo Bounded Context (Cargo & Tracking).
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Carga>()
            .HasMany(c => c.Asignaciones)
            .WithOne(a => a.Carga)
            .HasForeignKey(a => a.CargaId);

        modelBuilder.Entity<Carga>()
            .HasMany(c => c.Seguimientos)
            .WithOne(s => s.Carga)
            .HasForeignKey(s => s.CargaId);
    }
}