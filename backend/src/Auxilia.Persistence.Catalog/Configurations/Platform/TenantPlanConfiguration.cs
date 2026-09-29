using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations.Platform;

internal sealed class TenantPlanConfiguration : IEntityTypeConfiguration<TenantPlan>
{
    public void Configure(EntityTypeBuilder<TenantPlan> builder)
    {
        builder.ToTable("tenant_plans", table =>
            table.HasCheckConstraint("ck_tenant_plans_validity", "valid_to IS NULL OR valid_to > valid_from"));
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.Id).ValueGeneratedNever();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(plan => plan.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Plan>().WithMany().HasForeignKey(plan => plan.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(plan => new { plan.TenantId, plan.ValidFrom });
        builder.HasIndex(plan => plan.PlanId);
        builder.HasAuditColumns();
    }
}
