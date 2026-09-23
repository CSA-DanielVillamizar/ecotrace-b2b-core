using EcoTrace.CargoTracking.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrace.CargoTracking.Infrastructure.Configurations;

internal sealed class CargaConfiguration : IEntityTypeConfiguration<Carga>
{
    public void Configure(EntityTypeBuilder<Carga> builder)
    {
        builder.ToTable("Cargas");
        builder.HasKey(c => c.CargaId);
        builder.Property(c => c.CargaId).ValueGeneratedNever();
        builder.Property(c => c.Descripcion).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Origen).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Destino).HasMaxLength(120).IsRequired();
        builder.Property(c => c.PesoKg).IsRequired();
        builder.Property(c => c.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.CreadoEn).IsRequired();

        // Referencias externas a Identity: columnas planas, sin FK ni navegacion.
        builder.Property(c => c.GeneradorTenantId).IsRequired();
        builder.Property(c => c.TransportistaTenantId).IsRequired();
        builder.HasIndex(c => c.GeneradorTenantId);
        builder.HasIndex(c => c.TransportistaTenantId);
        builder.HasIndex(c => c.Estado);

        // Relaciones internas del agregado.
        builder.HasOne(c => c.Asignacion)
            .WithOne()
            .HasForeignKey<AsignacionCarga>(a => a.CargaId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Seguimientos)
            .WithOne()
            .HasForeignKey(s => s.CargaId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Seguimientos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class AsignacionCargaConfiguration : IEntityTypeConfiguration<AsignacionCarga>
{
    public void Configure(EntityTypeBuilder<AsignacionCarga> builder)
    {
        builder.ToTable("Asignaciones");
        builder.HasKey(a => a.AsignacionId);
        builder.Property(a => a.AsignacionId).ValueGeneratedNever();
        builder.Property(a => a.AsignadaEn).IsRequired();

        // Referencias externas a Fleet Management: columnas planas.
        builder.Property(a => a.VehiculoId).IsRequired();
        builder.Property(a => a.ConductorId).IsRequired();
        builder.HasIndex(a => a.VehiculoId);
        builder.HasIndex(a => a.ConductorId);
    }
}

internal sealed class SeguimientoConfiguration : IEntityTypeConfiguration<Seguimiento>
{
    public void Configure(EntityTypeBuilder<Seguimiento> builder)
    {
        builder.ToTable("Seguimientos");
        builder.HasKey(s => s.SeguimientoId);
        builder.Property(s => s.SeguimientoId).ValueGeneratedNever();
        builder.Property(s => s.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.Ubicacion).HasMaxLength(120).IsRequired();
        builder.Property(s => s.Nota).HasMaxLength(300);
        builder.Property(s => s.RegistradoEn).IsRequired();
        builder.HasIndex(s => s.CargaId);
    }
}
