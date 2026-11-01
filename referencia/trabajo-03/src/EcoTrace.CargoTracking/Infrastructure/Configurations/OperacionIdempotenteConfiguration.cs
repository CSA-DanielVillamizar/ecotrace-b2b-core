using EcoTrace.CargoTracking.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrace.CargoTracking.Infrastructure.Configurations;

internal sealed class OperacionIdempotenteConfiguration : IEntityTypeConfiguration<OperacionIdempotente>
{
    public void Configure(EntityTypeBuilder<OperacionIdempotente> builder)
    {
        builder.ToTable("OperacionesIdempotentes");
        builder.HasKey(o => o.OperacionId);
        builder.Property(o => o.Clave).HasMaxLength(100).IsRequired();
        builder.Property(o => o.Alcance).HasMaxLength(300).IsRequired();
        builder.Property(o => o.Huella).HasMaxLength(64).IsRequired();
        builder.Property(o => o.Respuesta).IsRequired();
        builder.Property(o => o.CreadaEn).IsRequired();

        // La clave es única por persona y ruta. Es lo que decide si dos solicitudes simultáneas son la misma acción.
        builder.HasIndex(o => new { o.Clave, o.Alcance }).IsUnique();
    }
}
