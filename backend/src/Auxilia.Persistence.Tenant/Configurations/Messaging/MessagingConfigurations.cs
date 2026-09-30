using Auxilia.Domain.Messaging;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Messaging;

internal sealed class MessagingAccountConfiguration : IEntityTypeConfiguration<MessagingAccount>
{
    public void Configure(EntityTypeBuilder<MessagingAccount> builder)
    {
        builder.ToTable("messaging_accounts", TenantSchemas.Configuration);
        builder.HasKey(account => account.Id);
        builder.Property(account => account.Id).ValueGeneratedNever();
        builder.Property(account => account.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(account => account.Provider).HasMaxLength(MessagingAccount.ProviderMaxLength);
        builder.Property(account => account.Name).HasMaxLength(MessagingAccount.NameMaxLength);
        builder.Property(account => account.SettingsJson).HasColumnName("settings").HasColumnType("jsonb");
        builder.Property(account => account.SecretProtected).HasMaxLength(4000);

        // Exactly one default account per channel (N03).
        builder.HasIndex(account => account.Channel).IsUnique().HasFilter("is_default").HasDatabaseName("ux_messaging_accounts_default_per_channel");
    }
}

internal sealed class SenderRuleConfiguration : IEntityTypeConfiguration<SenderRule>
{
    public void Configure(EntityTypeBuilder<SenderRule> builder)
    {
        builder.ToTable("sender_rules", TenantSchemas.Configuration);
        builder.HasKey(rule => rule.Id);
        builder.Property(rule => rule.Id).ValueGeneratedNever();
        builder.Property(rule => rule.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(rule => rule.Purpose).HasConversion<string>().HasMaxLength(20);
        builder.Property(rule => rule.Role).HasMaxLength(SenderRule.RoleMaxLength);
        builder.HasOne<MessagingAccount>().WithMany().HasForeignKey(rule => rule.AccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(rule => new { rule.Channel, rule.Purpose });
    }
}

internal sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        builder.ToTable("message_templates", TenantSchemas.Messaging);
        builder.HasKey(template => template.Id);
        builder.Property(template => template.Id).ValueGeneratedNever();
        builder.Property(template => template.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(template => template.Code).HasMaxLength(MessageTemplate.CodeMaxLength);
        builder.Property(template => template.Language).HasMaxLength(MessageTemplate.LanguageMaxLength);
        builder.Property(template => template.Subject).HasMaxLength(MessageTemplate.SubjectMaxLength);
        builder.HasIndex(template => new { template.Channel, template.Code, template.Language }).IsUnique();
    }
}

internal sealed class OutboundMessageConfiguration : IEntityTypeConfiguration<OutboundMessage>
{
    public void Configure(EntityTypeBuilder<OutboundMessage> builder)
    {
        builder.ToTable("outbound_messages", TenantSchemas.Messaging);
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Channel).HasConversion<string>().HasMaxLength(20);
        builder.Property(message => message.Purpose).HasConversion<string>().HasMaxLength(20);
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(message => message.Recipient).HasMaxLength(OutboundMessage.RecipientMaxLength);
        builder.Property(message => message.TemplateCode).HasMaxLength(MessageTemplate.CodeMaxLength);
        builder.Property(message => message.Language).HasMaxLength(MessageTemplate.LanguageMaxLength);
        builder.Property(message => message.Subject).HasMaxLength(MessageTemplate.SubjectMaxLength);
        builder.Property(message => message.ErrorCode).HasMaxLength(OutboundMessage.ErrorCodeMaxLength);
        builder.Property(message => message.RelatedEntityType).HasMaxLength(OutboundMessage.EntityTypeMaxLength);
        builder.HasOne<MessagingAccount>().WithMany().HasForeignKey(message => message.AccountId).OnDelete(DeleteBehavior.Restrict);

        // Outbound log (S-03): newest first, by status, by recipient, by related entity.
        builder.HasIndex(message => new { message.Status, message.QueuedAt });
        builder.HasIndex(message => message.QueuedAt);
        builder.HasIndex(message => message.Recipient);
        builder.HasIndex(message => new { message.RelatedEntityType, message.RelatedEntityId });
    }
}
