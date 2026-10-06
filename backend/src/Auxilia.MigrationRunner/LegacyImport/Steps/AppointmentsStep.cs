using Auxilia.Domain.Scheduling;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>E-05 (F13): <c>Appointments</c> → <c>scheduling.appointments</c> with one history row for the legacy status.</summary>
internal sealed class AppointmentsStep : ILegacyImportStep
{
    public const string Table = "Appointments";

    public string Name => "appointments";

    /// <summary>Legacy status text → <see cref="AppointmentStatus"/> (same names; "Concluded" is the legacy word for completed).</summary>
    public static AppointmentStatus? Status(string legacy) => legacy switch
    {
        "Pending" => AppointmentStatus.Pending,
        "Approved" => AppointmentStatus.Approved,
        "Rejected" => AppointmentStatus.Rejected,
        "Completed" or "Concluded" => AppointmentStatus.Completed,
        "Cancelled" => AppointmentStatus.Cancelled,
        _ => null,
    };

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var users = await MigratedUsers.LoadAsync(context, cancellationToken);
        var existing = await context.Tenant.Set<Appointment>().ToDictionaryAsync(appointment => appointment.Id, cancellationToken);
        var result = context.Report.For(Table);
        foreach (var legacy in await context.Legacy.Appointments.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            if (Status(legacy.Status) is not { } status)
            {
                context.Report.Skip(Table, legacy.Id, "unknown status");
                continue;
            }

            if (users.Client(context, legacy.ClientId) is not { } clientId)
            {
                context.Report.Skip(Table, legacy.Id, "client not migrated or not a client");
                continue;
            }

            if (users.User(context, legacy.EmployeeId) is not { } employeeId)
            {
                context.Report.Skip(Table, legacy.Id, "employee not migrated");
                continue;
            }

            var duration = Math.Clamp(legacy.DurationMinutes, Appointment.MinDurationMinutes, Appointment.MaxDurationMinutes);
            if (duration != legacy.DurationMinutes)
            {
                context.Report.Warn(Table, legacy.Id, $"duration out of range: set to {duration} minutes");
            }

            var notes = LegacyText.Fit(legacy.Notes, Appointment.NotesMaxLength, out var cut);
            if (cut)
            {
                context.Report.Warn(Table, legacy.Id, "notes too long: cut");
            }

            var customFields = CasesStep.CustomFields(legacy.CustomFields);
            if (customFields is null)
            {
                context.Report.Warn(Table, legacy.Id, "custom fields not a JSON object: left out");
            }

            var slot = new AppointmentSlot(LegacyImportContext.Instant(legacy.ScheduledDate), duration, notes, legacy.ShowInGlobalCalendare, customFields ?? "{}");
            if (context.Ids.Find(Table, legacy.Id) is { } id && existing.TryGetValue(id, out var current))
            {
                if (current.ApplyLegacyState(slot, status, context.Now))
                {
                    result.Updated++;
                }

                continue;
            }

            // The legacy has no "requested by" flag: a client's request starts Pending, staff book it Approved.
            var imported = Appointment.ImportLegacy(
                context.Ids.NewId(), clientId, employeeId, slot, status, requestedByClient: status == AppointmentStatus.Pending, LegacyImportContext.Instant(legacy.CreatedAt));
            if (imported.IsFailure)
            {
                context.Report.Skip(Table, legacy.Id, "the appointment is not valid");
                continue;
            }

            context.Tenant.Add(imported.Value);
            context.Ids.Add(Table, legacy.Id, imported.Value.Id);
            result.Created++;
        }
    }
}
