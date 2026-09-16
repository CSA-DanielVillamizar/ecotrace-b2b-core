using EcoTrace.Billing.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace EcoTrace.Billing.Infrastructure.Data;

public class BillingDbContext : DbContext
{
    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options)
    {
    }

    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<FinancialAudit> FinancialAudits => Set<FinancialAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("invoices");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Amount).HasPrecision(18, 2);
            entity.Property(i => i.Currency).HasMaxLength(3).IsRequired();
            entity.Property(i => i.IsDeleted).HasDefaultValue(false);
            entity.HasQueryFilter(i => !i.IsDeleted);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("payments");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Amount).HasPrecision(18, 2);
            entity.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            entity.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(p => p.IsDeleted).HasDefaultValue(false);
            entity.HasQueryFilter(p => !p.IsDeleted);
        });

        modelBuilder.Entity<FinancialAudit>(entity =>
        {
            entity.ToTable("financial_audits");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Action).HasMaxLength(100).IsRequired();
        });
    }
}
