using System.Text.Json;

using Auxilia.Application.Engagement.Public;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Engagement;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Imports;
using Auxilia.Domain.Scheduling;
using Auxilia.MigrationRunner.LegacyImport;
using Auxilia.Persistence.Tenant.Operations;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.LegacyImport.Tests;

/// <summary>E-05 end to end: appointments, requests, notifications, registrations and the import history.</summary>
[Collection(LegacyDatabaseGroup.Name)]
public sealed class ActivityImportTests(LegacyDatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Import_Activity_FollowsTheMapping_AndRepeats()
    {
        var connection = await fixture.CreateLegacyAsync(
            "legacy_activity", "legacy-security-update.sql",
            LegacyDatabaseFixture.Script("users.sql") + LegacyDatabaseFixture.Script("cases.sql") + LegacyDatabaseFixture.Script("activity.sql"));
        await using var source = (await LegacySource.OpenAsync(connection, Ct)).Value;
        await using var tenant = await fixture.CreateTenantAsync("tenant_activity");

        var report = await ImportHarness.ImportAsync(source, tenant);
        report.Reconciled.ShouldBeTrue(report.Render(dryRun: false));

        Result(report, "Appointments").ShouldBe((2, 0, 0, 2));
        Result(report, "Requests").ShouldBe((2, 0, 0, 1));
        Result(report, "Notifications").ShouldBe((2, 0, 1, 0));
        Result(report, "RegistrationRequests").ShouldBe((3, 0, 0, 1));
        Result(report, "ImportTypes").Created.ShouldBe(2);
        Result(report, "Imports").Created.ShouldBe(2);
        report.Issues.ShouldContain(issue => issue.Table == "Appointments" && issue.LegacyId == 2 && issue.Reason == "duration out of range: set to 5 minutes");
        report.Issues.ShouldContain(issue => issue.Table == "Appointments" && issue.LegacyId == 3 && issue.Reason == "unknown status");
        report.Issues.ShouldContain(issue => issue.Table == "Requests" && issue.LegacyId == 2 && issue.Reason == "unknown type: General");
        report.Issues.ShouldContain(issue => issue.Table == "RegistrationRequests" && issue.LegacyId == 4 && issue.Reason == "another pending request has the same e-mail");

        await using (var db = ImportHarness.Context(tenant))
        {
            var ids = await db.Set<LegacyIdMapping>().ToDictionaryAsync(row => (row.Entity, row.LegacyId), row => row.NewId, Ct);
            var (admin, employee, clientUser) = (ids[("Users", 1)], ids[("Users", 2)], ids[("Users", 3)]);
            var client = (await db.Set<User>().SingleAsync(user => user.Id == clientUser, Ct)).PersonId;
            var (pastId, futureId) = (ids[("Appointments", 1)], ids[("Appointments", 2)]);

            var past = await db.Set<Appointment>().SingleAsync(row => row.Id == pastId, Ct);
            (past.ClientId, past.EmployeeUserId, past.Status, past.StartsAt, past.DurationMinutes, past.ShowInGlobalCalendar, past.RequestedByClient)
                .ShouldBe((client, employee, AppointmentStatus.Completed, new DateTimeOffset(2025, 4, 10, 8, 30, 0, TimeSpan.Zero), 45, true, false));
            (await db.Set<Appointment>().SingleAsync(row => row.Id == futureId, Ct)).RequestedByClient.ShouldBeTrue();

            var (officeId, typedId) = (ids[("Requests", 1)], ids[("Requests", 2)]);
            var office = await db.Set<Request>().SingleAsync(row => row.Id == officeId, Ct);
            (office.RecipientUserId, office.Status, office.Type).ShouldBe(((Guid?)null, RequestStatus.Responded, RequestType.Information));
            office.Messages.Select(message => (message.AuthorUserId, message.Body)).ShouldBe([(clientUser, "Quando siete aperti?"), (admin, "Dal lunedì al venerdì")]);
            var typed = await db.Set<Request>().SingleAsync(row => row.Id == typedId, Ct);
            (typed.RecipientUserId, typed.Type, typed.Messages.Count).ShouldBe(((Guid?)employee, RequestType.General, 1));

            var notifications = await db.Set<Notification>().Where(row => row.UserId == clientUser).OrderBy(row => row.CreatedAt).ToListAsync(Ct);
            notifications.Select(row => (row.Kind, row.Link, row.IsRead)).ShouldBe([
                (NotificationKinds.LegacyMessage, $"/appointments?open={pastId}", true),
                (NotificationKinds.LegacyMessage, $"/cases/{ids[("Subscriptions", 1)]}", false)]);
            JsonSerializer.Deserialize<Dictionary<string, string>>(notifications[0].Parameters)!["title"].ShouldBe("Appuntamento");

            var registrations = await db.Set<RegistrationRequest>().OrderBy(row => row.RequestedAt).ToListAsync(Ct);
            registrations.Select(row => (row.Email, row.Status, row.ClientId, row.ProcessedByUserId)).ShouldBe([
                ("mario@example.test", RegistrationStatus.Approved, (Guid?)client, (Guid?)admin),
                ("gino@example.test", RegistrationStatus.Rejected, (Guid?)null, (Guid?)admin),
                ("lia@example.test", RegistrationStatus.Pending, (Guid?)null, (Guid?)null)]);
            registrations[0].BirthDate.ShouldBe(new DateOnly(1980, 5, 10));

            (await db.Set<ImportType>().OrderBy(row => row.Name).Select(row => new { row.Name, row.TargetEntity }).ToListAsync(Ct))
                .ShouldBe([new { Name = "Pratiche", TargetEntity = "Case" }, new { Name = "Pratiche (2)", TargetEntity = "Client" }]);
            (await db.Set<ImportJob>().OrderBy(row => row.CreatedAt).Select(row => new { row.Name, row.Status, HasFile = row.FileContent != null }).ToListAsync(Ct))
                .ShouldBe([new { Name = "Gennaio", Status = ImportJobStatus.Completed, HasFile = false }, new { Name = "clienti.xlsx", Status = ImportJobStatus.Cancelled, HasFile = false }]);
        }

        var again = await ImportHarness.ImportAsync(source, tenant);
        again.Reconciled.ShouldBeTrue(again.Render(dryRun: false));

        Result(again, "Appointments").Created.ShouldBe(0);
        Result(again, "Requests").Created.ShouldBe(0);
        Result(again, "Notifications").Created.ShouldBe(0);
        Result(again, "RegistrationRequests").Created.ShouldBe(0);
        Result(again, "Imports").Created.ShouldBe(0);
        await using (var db = ImportHarness.Context(tenant))
        {
            (await db.Set<Appointment>().CountAsync(Ct)).ShouldBe(2);
            (await db.Set<Notification>().CountAsync(Ct)).ShouldBe(2);
            (await db.Set<RegistrationRequest>().CountAsync(Ct)).ShouldBe(3);
            (await db.Set<ImportJob>().CountAsync(Ct)).ShouldBe(2);
        }
    }

    private static (int Created, int Updated, int Excluded, int Skipped) Result(LegacyImportReport report, string table)
    {
        var result = report.Tables[table];
        return (result.Created, result.Updated, result.Excluded, result.Skipped);
    }
}
