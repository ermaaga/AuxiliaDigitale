using Auxilia.Domain.Mailbox;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Mailbox;

internal sealed class SyncRunConfiguration : IEntityTypeConfiguration<SyncRun>
{
    public void Configure(EntityTypeBuilder<SyncRun> builder)
    {
        builder.ToTable("sync_runs", TenantSchemas.Mailbox, table =>
        {
            table.HasCheckConstraint("ck_sync_runs_scope", "scope IN ('Mail', 'Calendar')");
            table.HasCheckConstraint("ck_sync_runs_trigger", "trigger IN ('PageOpen', 'Manual', 'Scheduled')");
            table.HasCheckConstraint("ck_sync_runs_status", "status IN ('Queued', 'Running', 'Succeeded', 'Failed')");
        });
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id).ValueGeneratedNever();
        builder.Property(run => run.Scope).HasConversion<string>().HasMaxLength(20);
        builder.Property(run => run.Trigger).HasConversion<string>().HasMaxLength(20);
        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(run => run.ErrorCode).HasMaxLength(SyncRun.ErrorCodeMaxLength);
        builder.Property(run => run.TraceId).HasMaxLength(SyncRun.TraceIdMaxLength);
        builder.HasOne<ConnectedAccount>().WithMany().HasForeignKey(run => run.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(run => new { run.AccountId, run.Scope, run.QueuedAt });

        // One sync at a time per account and scope (#106).
        builder.HasIndex(run => new { run.AccountId, run.Scope })
            .IsUnique()
            .HasDatabaseName("ux_sync_runs_active")
            .HasFilter("status IN ('Queued', 'Running')");
    }
}

internal sealed class SyncCursorConfiguration : IEntityTypeConfiguration<SyncCursor>
{
    public void Configure(EntityTypeBuilder<SyncCursor> builder)
    {
        builder.ToTable("sync_cursors", TenantSchemas.Mailbox, table =>
            table.HasCheckConstraint("ck_sync_cursors_scope", "scope IN ('Mail', 'Calendar')"));
        builder.HasKey(cursor => new { cursor.AccountId, cursor.Scope, cursor.FolderKey });
        builder.Property(cursor => cursor.Scope).HasConversion<string>().HasMaxLength(20);
        builder.Property(cursor => cursor.FolderKey).HasMaxLength(SyncCursor.FolderKeyMaxLength);
        builder.Property(cursor => cursor.PageToken).HasMaxLength(SyncCursor.TokenMaxLength);
        builder.Property(cursor => cursor.SyncToken).HasMaxLength(SyncCursor.TokenMaxLength);
        builder.Ignore(cursor => cursor.IsFullSync);
        builder.HasOne<ConnectedAccount>().WithMany().HasForeignKey(cursor => cursor.AccountId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("calendar_events", TenantSchemas.Mailbox, table =>
            table.HasCheckConstraint("ck_calendar_events_range", "ends_at >= starts_at"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.ProviderEventId).HasMaxLength(CalendarEvent.ProviderEventIdMaxLength);
        builder.Property(item => item.Title).HasMaxLength(CalendarEvent.TitleMaxLength);
        builder.Property(item => item.Location).HasMaxLength(CalendarEvent.LocationMaxLength);
        builder.Property(item => item.Link).HasMaxLength(CalendarEvent.LinkMaxLength);
        builder.HasOne<ConnectedAccount>().WithMany().HasForeignKey(item => item.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => new { item.AccountId, item.ProviderEventId }).IsUnique();
        builder.HasIndex(item => new { item.AccountId, item.StartsAt });
    }
}

internal sealed class MailFolderConfiguration : IEntityTypeConfiguration<MailFolder>
{
    public void Configure(EntityTypeBuilder<MailFolder> builder)
    {
        builder.ToTable("mail_folders", TenantSchemas.Mailbox, table =>
            table.HasCheckConstraint("ck_mail_folders_kind", "kind IN ('Inbox', 'Sent')"));
        builder.HasKey(folder => folder.Id);
        builder.Property(folder => folder.Id).ValueGeneratedNever();
        builder.Property(folder => folder.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(folder => folder.ProviderFolderId).HasMaxLength(MailFolder.ProviderFolderIdMaxLength);
        builder.Property(folder => folder.Name).HasMaxLength(MailFolder.NameMaxLength);
        builder.Ignore(folder => folder.CursorKey);
        builder.HasOne<ConnectedAccount>().WithMany().HasForeignKey(folder => folder.AccountId).OnDelete(DeleteBehavior.Cascade);

        // One folder per account and kind: its cursor key (mail:inbox, mail:sent) survives a rename at the provider.
        builder.HasIndex(folder => new { folder.AccountId, folder.Kind }).IsUnique();
    }
}

internal sealed class MailMessageConfiguration : IEntityTypeConfiguration<MailMessage>
{
    public void Configure(EntityTypeBuilder<MailMessage> builder)
    {
        builder.ToTable("mail_messages", TenantSchemas.Mailbox, table =>
            table.HasCheckConstraint("ck_mail_messages_size", "size >= 0"));
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.ProviderMessageId).HasMaxLength(MailMessage.ProviderMessageIdMaxLength);
        builder.Property(message => message.InternetMessageId).HasMaxLength(MailMessage.InternetMessageIdMaxLength);
        builder.Property(message => message.ThreadId).HasMaxLength(MailMessage.ThreadIdMaxLength);
        builder.Property(message => message.FromAddress).HasMaxLength(MailMessage.AddressMaxLength);
        builder.Property(message => message.FromName).HasMaxLength(MailMessage.NameMaxLength);
        builder.Property(message => message.Subject).HasMaxLength(MailMessage.SubjectMaxLength);
        builder.Property(message => message.To).HasColumnName("to_recipients").HasColumnType("text[]");
        builder.Property(message => message.Cc).HasColumnName("cc_recipients").HasColumnType("text[]");
        builder.Property(message => message.AttachmentNames).HasColumnType("text[]");
        builder.HasOne<ConnectedAccount>().WithMany().HasForeignKey(message => message.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MailFolder>().WithMany().HasForeignKey(message => message.FolderId).OnDelete(DeleteBehavior.Cascade);

        // Unique per folder (IMAP UIDs are per folder; a Gmail message may be both in the inbox and in the sent mail).
        builder.HasIndex(message => new { message.FolderId, message.ProviderMessageId }).IsUnique();
        builder.HasIndex(message => new { message.FolderId, message.ReceivedAt });
        builder.HasIndex(message => new { message.AccountId, message.ReceivedAt });
        builder.HasIndex(message => message.InternetMessageId);
    }
}
