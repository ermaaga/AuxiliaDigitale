using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Catalog.Configurations.Platform;

internal sealed class PlatformUserConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> builder)
    {
        builder.ToTable("platform_users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.Email).HasColumnType("citext").HasMaxLength(PlatformUser.EmailMaxLength);
        builder.HasIndex(user => user.Email).IsUnique();
        builder.Property(user => user.DisplayName).HasMaxLength(PlatformUser.DisplayNameMaxLength);
        builder.Property(user => user.PasswordHash).HasMaxLength(500);
        builder.Property(user => user.TwoFactorSecret).HasMaxLength(1000);
        builder.Property(user => user.PendingTwoFactorSecret).HasMaxLength(1000);
        builder.Property(user => user.SecurityStamp).HasMaxLength(PlatformUser.SecurityStampLength);
        builder.Ignore(user => user.IsEnrolled);
        builder.Ignore(user => user.DomainEvents);
        builder.HasAuditColumns();
        builder.HasXminVersion();

        builder.OwnsMany(user => user.Roles, roles =>
        {
            roles.ToTable("platform_user_roles");
            roles.WithOwner().HasForeignKey(role => role.UserId);
            roles.HasKey(role => new { role.UserId, role.Role });
            roles.Property(role => role.Role).HasMaxLength(PlatformUserRole.RoleMaxLength);
        });
        builder.Navigation(user => user.Roles).HasField("roles");
    }
}
