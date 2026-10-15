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
