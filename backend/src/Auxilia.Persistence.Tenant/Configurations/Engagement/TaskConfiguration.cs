using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Engagement;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Engagement;

internal sealed class TaskConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("tasks", TenantSchemas.Engagement, table => table.HasCheckConstraint("ck_tasks_status", "status IN ('Open', 'Done')"));
        builder.HasKey(task => task.Id);
        builder.Property(task => task.Id).ValueGeneratedNever();
        builder.Property(task => task.Title).HasMaxLength(TaskItem.TitleMaxLength);
        builder.Property(task => task.Notes).HasMaxLength(TaskItem.NotesMaxLength);
        builder.Property(task => task.Status).HasConversion<string>().HasMaxLength(10);
        builder.HasOne<User>().WithMany().HasForeignKey(task => task.AssigneeUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(task => task.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(task => task.CompletedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(task => task.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Case>().WithMany().HasForeignKey(task => task.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(task => new { task.AssigneeUserId, task.Status, task.DueOn });
        builder.HasIndex(task => new { task.ClientId, task.CreatedAt });
        builder.HasIndex(task => task.CaseId);
        builder.HasIndex(task => task.CreatedByUserId);
        builder.HasIndex(task => task.CompletedByUserId);
    }
}

internal sealed class ClientActivityConfiguration : IEntityTypeConfiguration<ClientActivity>
{
    public void Configure(EntityTypeBuilder<ClientActivity> builder)
    {
        builder.ToTable("activities", TenantSchemas.Engagement, table =>
            table.HasCheckConstraint("ck_activities_kind", "kind IN ('Note', 'Call', 'Meeting', 'Email')"));
        builder.HasKey(activity => activity.Id);
        builder.Property(activity => activity.Id).ValueGeneratedNever();
        builder.Property(activity => activity.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(activity => activity.Text).HasMaxLength(ClientActivity.TextMaxLength);
        builder.HasOne<Person>().WithMany().HasForeignKey(activity => activity.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(activity => activity.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(activity => new { activity.ClientId, activity.OccurredAt });
        builder.HasIndex(activity => activity.AuthorUserId);
    }
}
