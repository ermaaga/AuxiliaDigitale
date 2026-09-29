using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations.Platform;

internal sealed class TenantDomainConfiguration : IEntityTypeConfiguration<TenantDomain>
{
    public void Configure(EntityTypeBuilder<TenantDomain> builder)
    {
        builder.ToTable("tenant_domains");
        builder.HasKey(domain => domain.Id);
        builder.Property(domain => domain.Id).ValueGeneratedNever();
        builder.Property(domain => domain.Host).HasColumnType("citext").HasMaxLength(TenantDomain.HostMaxLength);
        builder.HasIndex(domain => domain.Host).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(domain => domain.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(domain => domain.TenantId);
        builder.HasAuditColumns();
    }
}
