using EcoTrace.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrace.Identity.Infrastructure.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(t => t.TenantId);
        builder.Property(t => t.TenantId).ValueGeneratedNever();
        builder.Property(t => t.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(t => t.TenantType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.CreadoEn).IsRequired();
        builder.Property(t => t.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Version).IsRequired();
    }
}

internal sealed class AutorizacionPagoConfiguration : IEntityTypeConfiguration<AutorizacionPago>
{
    public void Configure(EntityTypeBuilder<AutorizacionPago> builder)
    {
        builder.ToTable("AutorizacionesPago");
        builder.HasKey(a => a.AutorizacionId);
        builder.Property(a => a.AutorizacionId).ValueGeneratedNever();
        builder.Property(a => a.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.CreadoEn).IsRequired();
        builder.Property(a => a.ActualizadoEn).IsRequired();

        // Referencia externa a Billing: columna plana. Una autorizacion por pago: es lo que hace
        // idempotente el paso 1 del Saga y lo que impide reutilizar una autorizacion revocada.
        builder.Property(a => a.PagoId).IsRequired();
        builder.HasIndex(a => a.PagoId).IsUnique();

        // El transportista si es de este contexto: clave foranea real.
        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
