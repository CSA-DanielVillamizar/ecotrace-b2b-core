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

        // Referencias externas a Identity: solo columnas. Ninguna relacion, ninguna FK.
        builder.Property(c => c.TenantId).IsRequired();
        builder.Property(c => c.RegistradoPorUserId).IsRequired();
        builder.Property(c => c.UserId);

        builder.HasIndex(c => c.TenantId);
        builder.HasIndex(c => new { c.TenantId, c.Licencia }).IsUnique();

        // El estado es token de concurrencia: dos reservas simultaneas del mismo recurso no pueden
        // ganar las dos. La segunda falla y la API responde 409.
        builder.Property(c => c.Estado).HasConversion<string>().HasMaxLength(20).IsRequired().IsConcurrencyToken();

        // Referencia externa a Cargo & Tracking: columna plana, sin FK.
        builder.Property(c => c.ReservadoParaCargaId);
        builder.HasIndex(c => c.ReservadoParaCargaId);
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

        builder.Property(v => v.TenantId).IsRequired();
        builder.Property(v => v.RegistradoPorUserId).IsRequired();

        builder.HasIndex(v => v.TenantId);
        builder.HasIndex(v => v.Placa).IsUnique();

        // El estado es token de concurrencia: dos reservas simultaneas del mismo recurso no pueden
        // ganar las dos. La segunda falla y la API responde 409.
        builder.Property(v => v.Estado).HasConversion<string>().HasMaxLength(20).IsRequired().IsConcurrencyToken();

        // Referencia externa a Cargo & Tracking: columna plana, sin FK.
        builder.Property(v => v.ReservadoParaCargaId);
        builder.HasIndex(v => v.ReservadoParaCargaId);
    }
}

internal sealed class NovedadConfiguration : IEntityTypeConfiguration<Novedad>
{
    public void Configure(EntityTypeBuilder<Novedad> builder)
    {
        builder.ToTable("Novedades");
        builder.HasKey(n => n.NovedadId);
        builder.Property(n => n.NovedadId).ValueGeneratedNever();

        // El vehículo es de este mismo contexto: relación real con clave foránea, sin propiedad de navegación.
        builder.HasOne<Vehiculo>().WithMany().HasForeignKey(n => n.VehiculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(n => n.VehiculoId);

        builder.Property(n => n.Tipo).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(n => n.Descripcion).HasMaxLength(Novedad.LargoMaximoDescripcion).IsRequired();
        builder.Property(n => n.OcurridaEn).IsRequired();
        builder.Property(n => n.RecibidaEn).IsRequired();

        // Referencias externas: columnas planas, sin FK.
        builder.Property(n => n.TenantId).IsRequired();
        builder.Property(n => n.ReportadaPorUserId).IsRequired();
        builder.Property(n => n.CargaId);
        builder.HasIndex(n => n.TenantId);
    }
}
