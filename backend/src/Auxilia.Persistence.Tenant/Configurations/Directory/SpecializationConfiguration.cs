using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Persistence.Tenant.Configurations.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Directory;

internal sealed class SpecializationConfiguration : IEntityTypeConfiguration<Specialization>
{
    public void Configure(EntityTypeBuilder<Specialization> builder)
    {
        builder.ToTable("specializations", TenantSchemas.Directory);
        builder.HasKey(specialization => specialization.Id);
        builder.Property(specialization => specialization.Id).ValueGeneratedNever();
        builder.Property(specialization => specialization.Name).HasMaxLength(Specialization.NameMaxLength).HasColumnType("citext");
        builder.Property(specialization => specialization.Role).HasConversion<string>().HasMaxLength(30);
        builder.HasOne<IdentityRoleRow>().WithMany().HasForeignKey(specialization => specialization.Role).OnDelete(DeleteBehavior.Restrict);
        builder.Property(specialization => specialization.Description).HasMaxLength(Specialization.DescriptionMaxLength);
        builder.Property(specialization => specialization.Email).HasMaxLength(Specialization.EmailMaxLength);
        builder.Property(specialization => specialization.WorkPhone).HasMaxLength(Specialization.WorkPhoneMaxLength);

        // One active specialization per role and name (deactivated ones keep their name).
        builder.HasIndex(specialization => new { specialization.Role, specialization.Name }).IsUnique().HasFilter("is_active");

        builder.OwnsMany(specialization => specialization.Members, members =>
        {
            members.ToTable("specialization_members", TenantSchemas.Directory);
            members.WithOwner().HasForeignKey(member => member.SpecializationId);
            members.HasKey(member => new { member.SpecializationId, member.UserId });
            members.HasOne<User>().WithMany().HasForeignKey(member => member.UserId).OnDelete(DeleteBehavior.Cascade);
            members.HasIndex(member => member.UserId);
        });
        builder.Navigation(specialization => specialization.Members).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
