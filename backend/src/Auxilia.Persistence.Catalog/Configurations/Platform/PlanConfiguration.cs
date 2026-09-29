using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations.Platform;

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public static readonly DateTimeOffset SeededAt = new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);

    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable("plans");
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.Id).ValueGeneratedNever();
        builder.Property(plan => plan.Code).HasMaxLength(Plan.CodeMaxLength);
        builder.HasIndex(plan => plan.Code).IsUnique();
        builder.Property(plan => plan.NameKey).HasMaxLength(Plan.NameKeyMaxLength);
        builder.HasIndex(plan => plan.IsDefault).IsUnique().HasFilter("is_default");
        builder.Ignore(plan => plan.DomainEvents);
        builder.HasAuditColumns();
        builder.HasXminVersion();

        builder.OwnsMany(plan => plan.Modules, modules =>
        {
            modules.ToTable("plan_modules");
            modules.WithOwner().HasForeignKey(module => module.PlanId);
            modules.HasKey(module => new { module.PlanId, module.ModuleCode });
            modules.Property(module => module.ModuleCode).HasMaxLength(PlatformModule.CodeMaxLength);
            modules.HasOne<PlatformModule>().WithMany().HasForeignKey(module => module.ModuleCode).OnDelete(DeleteBehavior.Restrict);
            modules.HasIndex(module => module.ModuleCode);
            modules.Property(module => module.Roles).HasRoleArrayConversion();
        });
        builder.Navigation(plan => plan.Modules).HasField("modules");

        // Default plan (N02): every module for every role; module rows are added by the module registry (P1-11).
        builder.HasData(new
        {
            Id = Plan.StandardId,
            Code = Plan.StandardCode,
            NameKey = "platform.plans.standard",
            IsDefault = true,
            IsActive = true,
            CreatedAt = SeededAt,
            CreatedBy = "seed",
        });
    }
}
