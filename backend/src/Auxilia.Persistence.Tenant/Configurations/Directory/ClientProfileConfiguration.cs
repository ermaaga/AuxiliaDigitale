using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Directory;

internal sealed class ClientProfileConfiguration : IEntityTypeConfiguration<ClientProfile>
{
    public void Configure(EntityTypeBuilder<ClientProfile> builder)
    {
        builder.ToTable("client_profiles", TenantSchemas.Directory, table =>
            table.HasCheckConstraint("ck_client_profiles_status", "status IN ('Inactive', 'Active')"));
        builder.HasKey(profile => profile.Id);
        builder.Property(profile => profile.Id).ValueGeneratedNever();
        builder.HasOne<Person>().WithOne().HasForeignKey<ClientProfile>(profile => profile.Id).OnDelete(DeleteBehavior.Restrict);
        builder.Property(profile => profile.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<User>().WithMany().HasForeignKey(profile => profile.EmployeeUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(profile => profile.EmployeeUserId);
        builder.HasIndex(profile => profile.Status);

        builder.OwnsMany(profile => profile.Assignments, assignments =>
        {
            assignments.ToTable("client_assignments", TenantSchemas.Directory);
            assignments.WithOwner().HasForeignKey(assignment => assignment.ClientId);
            assignments.HasKey(assignment => assignment.Id);
            assignments.Property(assignment => assignment.Id).ValueGeneratedNever();
            assignments.HasOne<User>().WithMany().HasForeignKey(assignment => assignment.EmployeeUserId).OnDelete(DeleteBehavior.Restrict);
            assignments.HasIndex(assignment => assignment.EmployeeUserId);

            // At most one open assignment per client.
            assignments.HasIndex(assignment => assignment.ClientId).IsUnique().HasFilter("ended_at IS NULL");
        });
        builder.Navigation(profile => profile.Assignments).HasField("assignments").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
