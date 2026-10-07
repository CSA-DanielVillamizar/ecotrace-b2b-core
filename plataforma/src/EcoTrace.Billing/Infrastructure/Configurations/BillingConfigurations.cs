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
        builder.Property(s => s.Estado).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(s => s.EventoOrigen).HasMaxLength(40).IsRequired();
        builder.Property(s => s.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(s => s.Motivo).HasMaxLength(600);
        builder.Property(s => s.CreadoEn).IsRequired();
        builder.Property(s => s.ActualizadoEn).IsRequired();
        builder.Property(s => s.ProximoIntentoEn).IsRequired();

        // Si dos ejecutores avanzan el mismo Saga a la vez, el segundo falla al guardar y se aparta.
        builder.Property(s => s.Version).IsConcurrencyToken();

        // Referencias externas a Cargo & Tracking y Fleet Management: columnas planas.
        builder.Property(s => s.CargaId).IsRequired();
        builder.Property(s => s.VehiculoId).IsRequired();
        builder.Property(s => s.ConductorId).IsRequired();

        // Un solo Saga por pago: es la clave de negocio que hace idempotente la liberacion (ADR 0002,
        // "idempotencyKey = PaymentId:release"). El pago si es de este contexto: clave foranea real.
        builder.HasIndex(s => s.PagoId).IsUnique();
        builder.HasOne<Pago>().WithMany().HasForeignKey(s => s.PagoId).OnDelete(DeleteBehavior.Restrict);

        // El ejecutor busca siempre lo mismo: sagas activos cuya hora de reintento ya llego.
        builder.HasIndex(s => new { s.Estado, s.ProximoIntentoEn });

        builder.HasMany(s => s.Pasos).WithOne().HasForeignKey(p => p.SagaId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Pasos).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SagaPasoConfiguration : IEntityTypeConfiguration<SagaPaso>
{
    public void Configure(EntityTypeBuilder<SagaPaso> builder)
    {
        builder.ToTable("SagasPasos");
        builder.HasKey(p => p.PasoId);
        builder.Property(p => p.PasoId).ValueGeneratedNever();
        builder.Property(p => p.Nombre).HasMaxLength(40).IsRequired();
        builder.Property(p => p.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(p => p.Detalle).HasMaxLength(600);
        builder.Property(p => p.ActualizadoEn).IsRequired();
        builder.HasIndex(p => new { p.SagaId, p.Orden }).IsUnique();
    }
}

internal sealed class EventoRecibidoConfiguration : IEntityTypeConfiguration<EventoRecibido>
{
    public void Configure(EntityTypeBuilder<EventoRecibido> builder)
    {
        builder.ToTable("EventosRecibidos");
        builder.HasKey(e => e.EventoId);
        builder.Property(e => e.EventoId).ValueGeneratedNever();
        builder.Property(e => e.Tipo).HasMaxLength(60).IsRequired();
        builder.Property(e => e.RecibidoEn).IsRequired();
    }
}
