using Auxilia.Domain.Engagement;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Engagement;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications", TenantSchemas.Engagement);
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).ValueGeneratedNever();
        builder.Property(notification => notification.Kind).HasMaxLength(Notification.KindMaxLength);
        builder.Property(notification => notification.Link).HasMaxLength(Notification.LinkMaxLength);
        builder.Property(notification => notification.Parameters).HasColumnType("jsonb");
        builder.Ignore(notification => notification.IsRead);
        builder.HasOne<User>().WithMany().HasForeignKey(notification => notification.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(notification => new { notification.UserId, notification.CreatedAt });

        // The unread badge counts these rows only.
        builder.HasIndex(notification => notification.UserId).HasFilter("read_at IS NULL").HasDatabaseName("ix_notifications_user_id_unread");
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preferences", TenantSchemas.Engagement);
        builder.HasKey(preference => new { preference.UserId, preference.Kind });
        builder.Property(preference => preference.Kind).HasMaxLength(Notification.KindMaxLength);
        builder.HasOne<User>().WithMany().HasForeignKey(preference => preference.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
