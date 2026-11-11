using EcoTrace.FleetManagement.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrace.FleetManagement.Infrastructure.Configurations;

internal sealed class NovedadVehiculoConfiguration : IEntityTypeConfiguration<NovedadVehiculo>
{
    public void Configure(EntityTypeBuilder<NovedadVehiculo> builder)
    {
        builder.ToTable("NovedadesVehiculo");
        builder.HasKey(n => n.NovedadId);
        builder.Property(n => n.NovedadId).ValueGeneratedNever();
        builder.Property(n => n.Descripcion).HasMaxLength(500).IsRequired();
        builder.Property(n => n.ReportadaEn).IsRequired();
        builder.HasOne<Vehiculo>().WithMany().HasForeignKey(n => n.VehiculoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(n => n.VehiculoId);
    }
}
