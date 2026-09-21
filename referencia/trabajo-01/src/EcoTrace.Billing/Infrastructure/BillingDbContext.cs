using EcoTrace.Billing.Domain;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Infrastructure;

/// <summary>
/// Base de datos propia de Billing &amp; Escrow (billing.db). Es el contexto más sensible: mueve
/// dinero entre dos organizaciones y por eso el estado del Escrow es un token de concurrencia.
/// </summary>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<Pago> Pagos => Set<Pago>();

    public DbSet<Factura> Facturas => Set<Factura>();

    public DbSet<AuditoriaFinanciera> Auditorias => Set<AuditoriaFinanciera>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BillingDbContext).Assembly);
    }
}
