using EcoTrace.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrace.Identity.Infrastructure.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.UserId);
        builder.Property(u => u.UserId).ValueGeneratedNever();
        builder.Property(u => u.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(200).IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.CreadoEn).IsRequired();
        builder.Property(u => u.ContrasenaHash).HasMaxLength(200);

        // Claves foraneas reales: Tenant y Role pertenecen a este mismo Bounded Context.
        builder.HasOne(u => u.Tenant)
            .WithMany()
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(u => u.Role)
            .WithMany()
            .HasForeignKey(u => u.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TokenRefrescoConfiguration : IEntityTypeConfiguration<TokenRefresco>
{
    public void Configure(EntityTypeBuilder<TokenRefresco> builder)
    {
        builder.ToTable("TokensRefresco");
        builder.HasKey(t => t.TokenRefrescoId);
        builder.Property(t => t.TokenRefrescoId).ValueGeneratedNever();
        builder.Property(t => t.Hash).HasMaxLength(64).IsRequired();
        builder.HasIndex(t => t.Hash).IsUnique();
        builder.Property(t => t.CreadoEn).IsRequired();
        builder.Property(t => t.ExpiraEn).IsRequired();

        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
