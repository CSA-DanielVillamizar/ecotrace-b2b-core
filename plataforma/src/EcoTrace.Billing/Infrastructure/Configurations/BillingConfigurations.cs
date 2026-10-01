using EcoTrace.Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrace.Billing.Infrastructure.Configurations;

internal sealed class PagoConfiguration : IEntityTypeConfiguration<Pago>
{
    public void Configure(EntityTypeBuilder<Pago> builder)
    {
        builder.ToTable("Pagos");
        builder.HasKey(p => p.PagoId);
        builder.Property(p => p.PagoId).ValueGeneratedNever();
        builder.Property(p => p.Monto).HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.Moneda).HasMaxLength(3).IsRequired();
        builder.Property(p => p.CreadoEn).IsRequired();
        builder.Property(p => p.ActualizadoEn).IsRequired();

        // El estado del Escrow es token de concurrencia: si dos solicitudes intentan liberar o
        // reembolsar el mismo pago a la vez, la segunda falla en vez de mover el dinero dos veces.
        builder.Property(p => p.EstadoEscrow)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired()
            .IsConcurrencyToken();

        // Referencias externas a Identity y Cargo & Tracking: columnas planas.
        builder.Property(p => p.GeneradorTenantId).IsRequired();
        builder.Property(p => p.TransportistaTenantId).IsRequired();
        builder.Property(p => p.CargaId).IsRequired();
        builder.HasIndex(p => p.GeneradorTenantId);
        builder.HasIndex(p => p.TransportistaTenantId);

        // Un solo pago en Escrow por carga.
        builder.HasIndex(p => p.CargaId).IsUnique();

        builder.HasMany(p => p.Auditoria)
            .WithOne()
            .HasForeignKey(a => a.PagoId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(p => p.Auditoria).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class FacturaConfiguration : IEntityTypeConfiguration<Factura>
{
    public void Configure(EntityTypeBuilder<Factura> builder)
    {
        builder.ToTable("Facturas");
        builder.HasKey(f => f.FacturaId);
        builder.Property(f => f.FacturaId).ValueGeneratedNever();
        builder.Property(f => f.Numero).HasMaxLength(30).IsRequired();
        builder.Property(f => f.Monto).HasPrecision(18, 2).IsRequired();
        builder.Property(f => f.EmitidaEn).IsRequired();

        builder.Property(f => f.CargaId).IsRequired();
        builder.Property(f => f.GeneradorTenantId).IsRequired();
        builder.Property(f => f.TransportistaTenantId).IsRequired();
        builder.HasIndex(f => f.GeneradorTenantId);
        builder.HasIndex(f => f.TransportistaTenantId);

        builder.HasIndex(f => f.Numero).IsUnique();
        builder.HasIndex(f => f.CargaId).IsUnique();
    }
}

internal sealed class AuditoriaFinancieraConfiguration : IEntityTypeConfiguration<AuditoriaFinanciera>
{
    public void Configure(EntityTypeBuilder<AuditoriaFinanciera> builder)
    {
        builder.ToTable("AuditoriaFinanciera");
        builder.HasKey(a => a.AuditoriaId);
        builder.Property(a => a.AuditoriaId).ValueGeneratedNever();
        builder.Property(a => a.Accion).HasMaxLength(120).IsRequired();
        builder.Property(a => a.EstadoResultante).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.OcurridoEn).IsRequired();
        builder.HasIndex(a => a.PagoId);
    }
}

internal sealed class SagaLiberacionPagoConfiguration : IEntityTypeConfiguration<SagaLiberacionPago>
{
    public void Configure(EntityTypeBuilder<SagaLiberacionPago> builder)
    {
        builder.ToTable("SagasLiberacionPago");
        builder.HasKey(s => s.SagaId);
        builder.Property(s => s.SagaId).ValueGeneratedNever();
        builder.Property(s => s.Estado).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(s => s.Motivo).HasMaxLength(500);
        builder.Property(s => s.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(s => s.Version).IsConcurrencyToken();
        builder.Property(s => s.LeaseToken).HasMaxLength(32);
        builder.Property(s => s.CreadaEn).IsRequired();
        builder.Property(s => s.ActualizadaEn).IsRequired();

        builder.HasIndex(s => s.PagoId).IsUnique();
        builder.HasIndex(s => s.Estado);
        builder.HasOne<Pago>().WithMany().HasForeignKey(s => s.PagoId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(s => s.Pasos)
            .WithOne()
            .HasForeignKey(p => p.SagaId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Pasos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PasoSagaConfiguration : IEntityTypeConfiguration<PasoSaga>
{
    public void Configure(EntityTypeBuilder<PasoSaga> builder)
    {
        builder.ToTable("PasosSaga");
        builder.HasKey(p => p.PasoSagaId);
        builder.Property(p => p.PasoSagaId).ValueGeneratedNever();
        builder.Property(p => p.Nombre).HasMaxLength(40).IsRequired();
        builder.Property(p => p.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Detalle).HasMaxLength(500);
        builder.HasIndex(p => new { p.SagaId, p.Orden }).IsUnique();
    }
}

internal sealed class EventoEntregaConfirmadaConfiguration : IEntityTypeConfiguration<EventoEntregaConfirmada>
{
    public void Configure(EntityTypeBuilder<EventoEntregaConfirmada> builder)
    {
        builder.ToTable("EventosEntregaConfirmada");
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).HasMaxLength(128).ValueGeneratedNever();
        builder.Property(e => e.EventType).HasMaxLength(50).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(e => e.OccurredAt);
        builder.Property(e => e.RecibidoEn).IsRequired();
        builder.HasIndex(e => e.SagaId).IsUnique();
        builder.HasOne<SagaLiberacionPago>().WithOne().HasForeignKey<EventoEntregaConfirmada>(e => e.SagaId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
