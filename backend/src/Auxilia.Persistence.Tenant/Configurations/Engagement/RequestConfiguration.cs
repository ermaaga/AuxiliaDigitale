using Auxilia.Domain.Engagement;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Engagement;

internal sealed class RequestConfiguration : IEntityTypeConfiguration<Request>
{
    public void Configure(EntityTypeBuilder<Request> builder)
    {
        builder.ToTable("requests", TenantSchemas.Engagement, table =>
        {
            table.HasCheckConstraint("ck_requests_status", "status IN ('Pending', 'Responded', 'Closed')");
            table.HasCheckConstraint("ck_requests_type", "type IN ('Information', 'General', 'Support', 'Appointment')");
        });
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).ValueGeneratedNever();
        builder.Property(request => request.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(request => request.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(request => request.Subject).HasMaxLength(Request.SubjectMaxLength);
        builder.HasOne<User>().WithMany().HasForeignKey(request => request.SenderUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(request => request.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(request => request.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(request => new { request.SenderUserId, request.SentAt });
        builder.HasIndex(request => new { request.RecipientUserId, request.SentAt });
        builder.HasIndex(request => new { request.Status, request.SentAt });
        builder.HasIndex(request => request.ClosedByUserId);

        builder.OwnsMany(request => request.Messages, messages =>
        {
            messages.ToTable("request_messages", TenantSchemas.Engagement);
            messages.WithOwner().HasForeignKey(message => message.RequestId);
            messages.HasKey(message => message.Id);
            messages.Property(message => message.Id).ValueGeneratedNever();
            messages.Property(message => message.Body).HasMaxLength(Request.BodyMaxLength);
            messages.HasOne<User>().WithMany().HasForeignKey(message => message.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
            messages.HasIndex(message => new { message.RequestId, message.Sequence }).IsUnique();
            messages.HasIndex(message => message.AuthorUserId);
        });
        builder.Navigation(request => request.Messages).HasField("messages").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
