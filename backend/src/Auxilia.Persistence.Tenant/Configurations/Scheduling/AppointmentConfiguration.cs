using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Scheduling;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Auxilia.Persistence.Tenant.Configurations.Scheduling;

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments", TenantSchemas.Scheduling, table =>
        {
            table.HasCheckConstraint("ck_appointments_status", "status IN ('Pending', 'Approved', 'Rejected', 'Completed', 'Cancelled')");
            table.HasCheckConstraint("ck_appointments_duration", $"duration_minutes BETWEEN {Appointment.MinDurationMinutes} AND {Appointment.MaxDurationMinutes}");
            table.HasCheckConstraint("ck_appointments_ends_at", "ends_at > starts_at");
        });
        builder.HasKey(appointment => appointment.Id);
        builder.Property(appointment => appointment.Id).ValueGeneratedNever();
        builder.Property(appointment => appointment.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(appointment => appointment.Notes).HasMaxLength(Appointment.NotesMaxLength);
        builder.Property(appointment => appointment.CustomFields).HasColumnType("jsonb");
        builder.HasOne<ClientProfile>().WithMany().HasForeignKey(appointment => appointment.ClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(appointment => appointment.EmployeeUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(appointment => new { appointment.ClientId, appointment.StartsAt });
        builder.HasIndex(appointment => new { appointment.EmployeeUserId, appointment.StartsAt });
        builder.HasIndex(appointment => appointment.StartsAt);

        builder.OwnsMany(appointment => appointment.History, history =>
        {
            history.ToTable("appointment_status_history", TenantSchemas.Scheduling);
            history.WithOwner().HasForeignKey(change => change.AppointmentId);
            history.HasKey(change => change.Id);
            history.Property(change => change.Id).ValueGeneratedNever();
            history.Property(change => change.FromStatus).HasConversion<string>().HasMaxLength(20);
            history.Property(change => change.ToStatus).HasConversion<string>().HasMaxLength(20);
            history.Property(change => change.Note).HasMaxLength(Appointment.NoteMaxLength);
            history.HasOne<User>().WithMany().HasForeignKey(change => change.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
            history.HasIndex(change => new { change.AppointmentId, change.Sequence }).IsUnique();
            history.HasIndex(change => change.ChangedByUserId);
        });
        builder.Navigation(appointment => appointment.History).HasField("history").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
