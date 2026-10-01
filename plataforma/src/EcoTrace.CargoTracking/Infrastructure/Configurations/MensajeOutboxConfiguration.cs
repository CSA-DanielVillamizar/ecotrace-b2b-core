using EcoTrace.CargoTracking.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrace.CargoTracking.Infrastructure.Configurations;

/// <summary>
/// Mapeo de MensajeOutbox a la tabla MensajesOutbox, siguiendo el mismo patrón que
/// CargaConfiguration, AsignacionCargaConfiguration y SeguimientoConfiguration: una clase de
/// configuración por entidad, recogida automáticamente por
/// <c>ApplyConfigurationsFromAssembly</c> en CargoTrackingDbContext.
/// </summary>
internal sealed class MensajeOutboxConfiguration : IEntityTypeConfiguration<MensajeOutbox>
{
    public void Configure(EntityTypeBuilder<MensajeOutbox> builder)
    {
        builder.ToTable("MensajesOutbox");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.EventId).HasMaxLength(64).IsRequired();

        // Segunda capa de protección para la idempotencia: aunque la lógica de
        // aplicación ya evita crear un EventId repetido, este índice único hace
        // que la propia base de datos lo rechace si algo fallara -- el mismo
        // principio de "cinturón y tirantes" que ya usa el proyecto con las
        // violaciones de unicidad que traduce GuardarAsync a 409.
        builder.HasIndex(m => m.EventId).IsUnique();

        builder.Property(m => m.CorrelationId).HasMaxLength(64).IsRequired();

        // Igual que Carga.Estado y Seguimiento.Estado: el enum se guarda como
        // texto ("Pendiente", no "0"), para que la base de datos sea legible
        // directamente y no dependa de recordar qué número es cada estado.
        builder.Property(m => m.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(m => m.UltimoError).HasMaxLength(500);
        builder.Property(m => m.OcurrioEn).IsRequired();
        builder.Property(m => m.ProximoIntentoEn).IsRequired();

        // Referencias externas: columnas planas, sin FK ni navegación -- igual
        // que GeneradorTenantId/TransportistaTenantId en CargaConfiguration.
        builder.Property(m => m.CargaId).IsRequired();
        builder.Property(m => m.VehiculoId).IsRequired();
        builder.Property(m => m.ConductorId).IsRequired();
        builder.Property(m => m.GeneradorTenantId).IsRequired();
        builder.Property(m => m.TransportistaTenantId).IsRequired();

        // El publicador SIEMPRE consulta por (Estado, ProximoIntentoEn) -- ver
        // OutboxPublicador.PublicarPendientesAsync. Este índice compuesto evita
        // que esa consulta, que corre cada 1 segundo, escanee toda la tabla.
        builder.HasIndex(m => new { m.Estado, m.ProximoIntentoEn });
    }
}
