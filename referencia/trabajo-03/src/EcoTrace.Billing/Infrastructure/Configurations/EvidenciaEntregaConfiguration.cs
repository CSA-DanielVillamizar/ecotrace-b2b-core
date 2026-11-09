using EcoTrace.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrace.Billing.Infrastructure.Configurations;

internal sealed class EvidenciaEntregaConfiguration : IEntityTypeConfiguration<EvidenciaEntrega>
{
    public void Configure(EntityTypeBuilder<EvidenciaEntrega> builder)
    {
        builder.ToTable("EvidenciasEntrega");
        builder.HasKey(e => e.EvidenciaId);
        builder.Property(e => e.EvidenciaId).ValueGeneratedNever();
        builder.Property(e => e.Descripcion).HasMaxLength(500).IsRequired();
        builder.Property(e => e.Referencia).HasMaxLength(200);
        builder.Property(e => e.RegistradaEn).IsRequired();
        builder.HasOne<Pago>().WithMany().HasForeignKey(e => e.PagoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.PagoId);
    }
}
