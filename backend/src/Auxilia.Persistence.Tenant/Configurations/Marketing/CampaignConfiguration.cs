using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Marketing;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Marketing;

internal sealed class EmailTemplateConfiguration : IEntityTypeConfiguration<EmailTemplate>
{
    public void Configure(EntityTypeBuilder<EmailTemplate> builder)
    {
        builder.ToTable("email_templates", TenantSchemas.Marketing);
        builder.HasKey(template => template.Id);
        builder.Property(template => template.Id).ValueGeneratedNever();
        builder.Property(template => template.Name).HasMaxLength(EmailTemplate.NameMaxLength).HasColumnType("citext");
        builder.Property(template => template.Language).HasMaxLength(EmailTemplate.LanguageMaxLength);
        builder.Property(template => template.Subject).HasMaxLength(EmailTemplate.SubjectMaxLength);
        builder.HasIndex(template => template.Name).IsUnique();
    }
}

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("campaigns", TenantSchemas.Marketing, table =>
        {
            table.HasCheckConstraint("ck_campaigns_status", "status IN ('Draft', 'Sending', 'Sent', 'Cancelled', 'Failed')");
            table.HasCheckConstraint("ck_campaigns_audience", "(segment_id IS NULL) <> (list_id IS NULL)");
        });
        builder.HasKey(campaign => campaign.Id);
        builder.Property(campaign => campaign.Id).ValueGeneratedNever();
        builder.Property(campaign => campaign.Name).HasMaxLength(Campaign.NameMaxLength);
        builder.Property(campaign => campaign.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(campaign => campaign.ErrorCode).HasMaxLength(20);
        builder.Property(campaign => campaign.ErrorMessage).HasMaxLength(Campaign.ErrorMaxLength);
        builder.HasOne<EmailTemplate>().WithMany().HasForeignKey(campaign => campaign.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Segment>().WithMany().HasForeignKey(campaign => campaign.SegmentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<StaticList>().WithMany().HasForeignKey(campaign => campaign.ListId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(campaign => campaign.SentByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(campaign => campaign.TemplateId);
        builder.HasIndex(campaign => campaign.SegmentId);
        builder.HasIndex(campaign => campaign.ListId);
        builder.HasIndex(campaign => campaign.SentByUserId);
    }
}

internal sealed class CampaignRecipientConfiguration : IEntityTypeConfiguration<CampaignRecipient>
{
    public void Configure(EntityTypeBuilder<CampaignRecipient> builder)
    {
        builder.ToTable("campaign_recipients", TenantSchemas.Marketing, table =>
        {
            table.HasCheckConstraint("ck_campaign_recipients_status", "status IN ('Pending', 'Sent', 'Failed', 'Excluded')");
            table.HasCheckConstraint("ck_campaign_recipients_exclusion", "exclusion IS NULL OR exclusion IN ('NoConsent', 'NoEmail', 'Suppressed', 'Cancelled')");
        });
        builder.HasKey(recipient => new { recipient.CampaignId, recipient.PersonId });
        builder.Property(recipient => recipient.Email).HasMaxLength(Suppression.EmailMaxLength);
        builder.Property(recipient => recipient.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(recipient => recipient.Exclusion).HasConversion<string>().HasMaxLength(20);
        builder.Property(recipient => recipient.ErrorCode).HasMaxLength(20);
        builder.HasOne<Campaign>().WithMany().HasForeignKey(recipient => recipient.CampaignId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Person>().WithMany().HasForeignKey(recipient => recipient.PersonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(recipient => new { recipient.CampaignId, recipient.Status });
        builder.HasIndex(recipient => recipient.PersonId);
    }
}

internal sealed class SuppressionConfiguration : IEntityTypeConfiguration<Suppression>
{
    public void Configure(EntityTypeBuilder<Suppression> builder)
    {
        builder.ToTable("suppressions", TenantSchemas.Marketing);
        builder.HasKey(suppression => suppression.Id);
        builder.Property(suppression => suppression.Id).ValueGeneratedNever();
        builder.Property(suppression => suppression.Email).HasMaxLength(Suppression.EmailMaxLength).HasColumnType("citext");
        builder.Property(suppression => suppression.Reason).HasMaxLength(Suppression.ReasonMaxLength);
        builder.HasIndex(suppression => suppression.Email).IsUnique();
    }
}
