using EcoTrace.CargoTracking.Domain;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.CargoTracking.Infrastructure;

/// <summary>
/// Base de datos propia de Cargo &amp; Tracking (cargotracking.db). Las claves foraneas de este
/// contexto solo unen entidades propias (Carga, Asignacion, Seguimiento, MensajeOutbox); los
/// identificadores de Identity y Fleet Management son columnas planas -- ver
/// docs/decisiones-de-diseno.md, decisión 4: "la separación física es la prueba más directa de
/// que ningún contexto mira la base de otro".
/// </summary>
public sealed class CargoTrackingDbContext(DbContextOptions<CargoTrackingDbContext> options) : DbContext(options)
{
    public DbSet<Carga> Cargas => Set<Carga>();

    public DbSet<AsignacionCarga> Asignaciones => Set<AsignacionCarga>();

    public DbSet<Seguimiento> Seguimientos => Set<Seguimiento>();

    /// <summary>
    /// Trabajo 2 (patrón Outbox, ADR 0002): tabla independiente, sin relación de navegación con
    /// Carga. Se llena en la MISMA transacción que un Seguimiento en estado Entregado -- eso se
    /// garantiza en CargasController.RegistrarSeguimiento con un solo GuardarAsync, no aquí en
    /// el DbContext.
    /// </summary>
    public DbSet<MensajeOutbox> MensajesOutbox => Set<MensajeOutbox>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Recoge automáticamente TODAS las clases IEntityTypeConfiguration<T> del
        // ensamblado, incluida la nueva MensajeOutboxConfiguration -- por eso
        // agregar la entidad del Trabajo 2 no obligó a tocar este método, solo
        // a agregar el DbSet de arriba y su configuración en un archivo aparte.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CargoTrackingDbContext).Assembly);
    }
}
