using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Directory;

internal sealed class EmployeeProfileConfiguration : IEntityTypeConfiguration<EmployeeProfile>
{
    public void Configure(EntityTypeBuilder<EmployeeProfile> builder)
    {
        builder.ToTable("employee_profiles", TenantSchemas.Directory);
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).ValueGeneratedNever();
        builder.HasOne<User>().WithOne().HasForeignKey<EmployeeProfile>(profile => profile.Id).OnDelete(DeleteBehavior.Restrict);
        builder.Property(profile => profile.IsDefault).HasDefaultValue(false);
        builder.HasOne<User>().WithMany().HasForeignKey(profile => profile.AdministratorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(profile => profile.AdministratorUserId);

        // Q31: at most one default employee.
        builder.HasIndex(profile => profile.IsDefault).IsUnique().HasFilter("is_default").HasDatabaseName("ux_employee_profiles_default");
    }
}
