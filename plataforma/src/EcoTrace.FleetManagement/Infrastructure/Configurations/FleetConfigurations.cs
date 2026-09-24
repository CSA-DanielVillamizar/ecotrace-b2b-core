using EcoTrace.FleetManagement.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrace.FleetManagement.Infrastructure.Configurations;

internal sealed class ConductorConfiguration : IEntityTypeConfiguration<Conductor>
{
    public void Configure(EntityTypeBuilder<Conductor> builder)
    {
        builder.ToTable("Conductores");
        builder.HasKey(c => c.ConductorId);
        builder.Property(c => c.ConductorId).ValueGeneratedNever();
        builder.Property(c => c.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(c => c.Licencia).HasMaxLength(20).IsRequired();
        builder.Property(c => c.CreadoEn).IsRequired();

        // El estado es token de concurrencia: si dos solicitudes reservan al mismo conductor a la
        // vez, la segunda falla al guardar en lugar de pisar la reserva de la primera.
        builder.Property(c => c.Estado)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .IsConcurrencyToken();

        // Referencias externas a Identity y a Cargo & Tracking: solo columnas. Ninguna relacion,
        // ninguna FK.
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.RegistradoPorUserId).IsRequired();
        builder.Property(c => c.UserId);
        builder.Property(c => c.ReservadoParaCargaId);

        builder.HasIndex(c => c.TenantId);
        builder.HasIndex(c => c.ReservadoParaCargaId);
        builder.HasIndex(c => new { c.TenantId, c.Licencia }).IsUnique();
    }
}

internal sealed class VehiculoConfiguration : IEntityTypeConfiguration<Vehiculo>
{
    public void Configure(EntityTypeBuilder<Vehiculo> builder)
    {
        builder.ToTable("Vehiculos");
        builder.HasKey(v => v.VehiculoId);
        builder.Property(v => v.VehiculoId).ValueGeneratedNever();
        builder.Property(v => v.Placa).HasMaxLength(8).IsRequired();
        builder.Property(v => v.CapacidadKg).IsRequired();
        builder.Property(v => v.CreadoEn).IsRequired();

        builder.Property(v => v.Estado)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .IsConcurrencyToken();

        builder.Property(v => v.TenantId).IsRequired();
        builder.Property(v => v.RegistradoPorUserId).IsRequired();
        builder.Property(v => v.ReservadoParaCargaId);

        builder.HasIndex(v => v.TenantId);
        builder.HasIndex(v => v.ReservadoParaCargaId);
        builder.HasIndex(v => v.Placa).IsUnique();
    }
}
