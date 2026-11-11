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
        // Token de concurrencia: dos solicitudes que parten del mismo estado no pueden ganar las dos,
        // y asi una entrega simultanea no genera dos mensajes EntregaConfirmada en el Outbox.
        builder.Property(c => c.Estado).HasConversion<string>().HasMaxLength(20).IsRequired().IsConcurrencyToken();
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

internal sealed class OutboxMensajeConfiguration : IEntityTypeConfiguration<OutboxMensaje>
{
    public void Configure(EntityTypeBuilder<OutboxMensaje> builder)
    {
        builder.ToTable("Outbox");
        builder.HasKey(m => m.EventoId);
        builder.Property(m => m.EventoId).ValueGeneratedNever();
        builder.Property(m => m.Tipo).HasMaxLength(60).IsRequired();
        builder.Property(m => m.Contenido).HasMaxLength(2000).IsRequired();
        builder.Property(m => m.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(m => m.Estado).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.Intentos).IsRequired();
        builder.Property(m => m.CreadoEn).IsRequired();
        builder.Property(m => m.ProximoIntentoEn).IsRequired();
        builder.Property(m => m.UltimoError).HasMaxLength(300);

        // El publicador busca siempre lo mismo: pendientes cuya hora de reintento ya llego.
        builder.HasIndex(m => new { m.Estado, m.ProximoIntentoEn });
    }
}

/// <summary>
/// Le explica a la base de datos cómo guardar los apuntes de operaciones ya procesadas: la tabla
/// <c>OperacionesProcesadas</c> (Trabajo 3, ADR 0005). Sirve para que un reintento con el mismo número no repita el efecto.
/// </summary>
internal sealed class OperacionProcesadaConfiguration : IEntityTypeConfiguration<OperacionProcesada>
{
    /// <summary>Define el nombre de la tabla, su llave, sus columnas y su relación con la carga.</summary>
    public void Configure(EntityTypeBuilder<OperacionProcesada> builder)
    {
        builder.ToTable("OperacionesProcesadas");

        // La llave de la tabla ES el número de la operación. Esa es la protección de verdad: si dos solicitudes con
        // el mismo número llegan al mismo tiempo, la base de datos deja pasar solo una. Lo que revisa el controlador
        // antes es solo una comodidad; esta regla de la base de datos es la que no se puede saltar.
        builder.HasKey(o => o.OperacionId);
        builder.Property(o => o.OperacionId).ValueGeneratedNever();

        // El resultado es la respuesta de la primera vez, en texto. No tiene largo máximo porque crece
        // con la cantidad de seguimientos de la carga.
        builder.Property(o => o.Resultado).IsRequired();
        builder.Property(o => o.ProcesadaEn).IsRequired();

        // La operación pertenece a una carga de este mismo módulo, así que se une con una llave foránea (la columna
        // CargaId apunta a la tabla Cargas). La prueba de arquitectura lo exige: todo identificador debe ser la llave
        // de la tabla, una llave foránea interna, o una referencia a otro módulo declarada. Este es el segundo caso.
        builder.HasOne<Carga>()
            .WithMany()
            .HasForeignKey(o => o.CargaId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(o => o.CargaId);
    }
}
