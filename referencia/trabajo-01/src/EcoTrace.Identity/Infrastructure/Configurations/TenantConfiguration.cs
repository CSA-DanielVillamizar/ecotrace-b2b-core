using EcoTrace.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EcoTrace.Identity.Infrastructure.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(t => t.TenantId);
        builder.Property(t => t.TenantId).ValueGeneratedNever();
        builder.Property(t => t.Nombre).HasMaxLength(120).IsRequired();
        builder.Property(t => t.TenantType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.CreadoEn).IsRequired();
    }
}
