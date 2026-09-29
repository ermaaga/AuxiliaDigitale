using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations.Platform;

internal sealed class TenantModuleOverrideConfiguration : IEntityTypeConfiguration<TenantModuleOverride>
{
    public void Configure(EntityTypeBuilder<TenantModuleOverride> builder)
    {
        builder.ToTable("tenant_module_overrides");
        builder.HasKey(item => new { item.TenantId, item.ModuleCode });
        builder.Property(item => item.ModuleCode).HasMaxLength(PlatformModule.CodeMaxLength);
        builder.Property(item => item.Roles).HasRoleArrayConversion();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(item => item.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PlatformModule>().WithMany().HasForeignKey(item => item.ModuleCode).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => item.ModuleCode);
        builder.HasAuditColumns();
        builder.HasXminVersion();
    }
}
