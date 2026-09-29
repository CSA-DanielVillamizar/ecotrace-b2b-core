using EcoTrace.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrace.Identity.Infrastructure.Configurations;

internal sealed class AutorizacionPagoConfiguration : IEntityTypeConfiguration<AutorizacionPago>
{
    public void Configure(EntityTypeBuilder<AutorizacionPago> builder)
    {
        builder.ToTable("AutorizacionesPago");
        builder.HasKey(a => a.AutorizacionId);
        builder.Property(a => a.AutorizacionId).ValueGeneratedNever();
        builder.Property(a => a.PagoId).IsRequired();
        // Una autorizacion por pago (contrato del Trabajo 2): esto es lo que traduce GuardarAsync
        // a 409 si dos solicitudes concurrentes intentan crear la misma.
        builder.HasIndex(a => a.PagoId).IsUnique();
        builder.Property(a => a.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.CreadoEn).IsRequired();
        builder.Property(a => a.ActualizadoEn).IsRequired();

        // TenantId es clave foranea real: Tenant vive en este mismo Bounded Context.
        builder.HasOne(a => a.Tenant)
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
