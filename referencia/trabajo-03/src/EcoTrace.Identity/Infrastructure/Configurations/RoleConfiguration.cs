using EcoTrace.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrace.Identity.Infrastructure.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(r => r.RoleId);
        builder.Property(r => r.RoleId).ValueGeneratedNever();
        builder.Property(r => r.Nombre).HasMaxLength(40).IsRequired();
        builder.HasIndex(r => r.Nombre).IsUnique();

        builder.HasMany(r => r.Claims)
            .WithOne()
            .HasForeignKey(c => c.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        // Datos de referencia: los cuatro roles del ADR 0001.
        builder.HasData(
            new Role(1, "Conductor"),
            new Role(2, "Supervisor"),
            new Role(3, "Administrador"),
            new Role(4, "Auditor"));
    }
}

internal sealed class RoleClaimConfiguration : IEntityTypeConfiguration<RoleClaim>
{
    public void Configure(EntityTypeBuilder<RoleClaim> builder)
    {
        builder.ToTable("RoleClaims");
        builder.HasKey(c => c.RoleClaimId);
        builder.Property(c => c.RoleClaimId).ValueGeneratedNever();
        builder.Property(c => c.Tipo).HasMaxLength(40).IsRequired();
        builder.Property(c => c.Valor).HasMaxLength(80).IsRequired();

        builder.HasData(
            new RoleClaim(1, 1, "permiso", "cargas.confirmar_entrega"),
            new RoleClaim(2, 1, "permiso", "seguimiento.registrar"),
            new RoleClaim(3, 2, "permiso", "flota.gestionar"),
            new RoleClaim(4, 2, "permiso", "cargas.asignar"),
            new RoleClaim(5, 3, "permiso", "organizaciones.gestionar"),
            new RoleClaim(6, 3, "permiso", "usuarios.gestionar"),
            new RoleClaim(7, 4, "permiso", "auditoria.leer"));
    }
}
