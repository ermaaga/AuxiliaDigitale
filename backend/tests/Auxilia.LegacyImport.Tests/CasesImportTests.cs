using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Images;
using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.MigrationRunner.LegacyImport;
using Auxilia.MigrationRunner.LegacyImport.Steps;
using Auxilia.Persistence.Tenant;
using Auxilia.Persistence.Tenant.Interceptors;
using Auxilia.Persistence.Tenant.Operations;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using NSubstitute;

using Case = Auxilia.Domain.Cases.Case;

namespace Auxilia.LegacyImport.Tests;

/// <summary>E-03 end to end: categories, services, folders and cases with payments, numbers and client status.</summary>
[Collection(LegacyDatabaseGroup.Name)]
public sealed class CasesImportTests(LegacyDatabaseFixture fixture)
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Import_CatalogAndCases_FollowTheMapping_AndRepeats()
    {
        var connection = await fixture.CreateLegacyAsync(
            "legacy_cases", "legacy-security-update.sql", LegacyDatabaseFixture.Script("users.sql") + LegacyDatabaseFixture.Script("cases.sql"));
        await using var source = (await LegacySource.OpenAsync(connection, Ct)).Value;
        await using var tenant = await fixture.CreateTenantAsync("tenant_cases");

        var report = await ImportAsync(source, tenant);

        Result(report, "MembershipTypes").ShouldBe((2, 0, 0));
        Result(report, "Memberships").ShouldBe((2, 0, 0));
        Result(report, "MembershipFolderTemplates").ShouldBe((2, 0, 1));
        Result(report, "Subscriptions").ShouldBe((2, 0, 2));
        report.Issues.ShouldContain(issue => issue.Table == "MembershipTypes" && issue.LegacyId == 2 && issue.Reason.StartsWith("name already used", StringComparison.Ordinal));
        report.Issues.ShouldContain(issue => issue.Table == "Memberships" && issue.LegacyId == 2 && issue.Reason == "duration out of range: set to 1 days");
        report.Issues.ShouldContain(issue => issue.Table == "MembershipFolderTemplates" && issue.LegacyId == 3 && issue.Reason == "parent folder not migrated");
        report.Issues.ShouldContain(issue => issue.Table == "Subscriptions" && issue.LegacyId == 3 && issue.Reason == "client not migrated or not a client");
        report.Issues.ShouldContain(issue => issue.Table == "Subscriptions" && issue.LegacyId == 4 && issue.Reason == "unknown status 7");

        await using (var db = Context(tenant))
        {
            var ids = await db.Set<LegacyIdMapping>().ToDictionaryAsync(row => (row.Entity, row.LegacyId), row => row.NewId, Ct);
            var (serviceId, clientUserId) = (ids[("Memberships", 1)], ids[("Users", 3)]);
            var (openId, completedId) = (ids[("Subscriptions", 1)], ids[("Subscriptions", 2)]);
            (await db.Set<ServiceCategory>().OrderBy(category => category.Name).Select(category => category.Name).ToListAsync(Ct)).ShouldBe(["Fiscale", "Fiscale (2)"]);

            var service = await db.Set<Service>().SingleAsync(row => row.Id == serviceId, Ct);
            (service.Name, service.Price, service.DurationDays, service.CategoryId, service.SpecializationId)
                .ShouldBe(("730", 150.00m, 365, (Guid?)ids[("MembershipTypes", 1)], (Guid?)ids[("RoleSpecializations", 1)]));
            var folders = await db.Set<ServiceFolder>().Where(folder => folder.ServiceId == service.Id).ToListAsync(Ct);
            folders.Single(folder => folder.Name == "Redditi").ParentId.ShouldBe(ids[("MembershipFolderTemplates", 1)]);

            var client = await db.Set<User>().SingleAsync(user => user.Id == clientUserId, Ct);
            var open = await db.Set<Case>().SingleAsync(row => row.Id == openId, Ct);
            (open.Number, open.ClientId, open.Status, open.StartedOn, open.Price, open.SpecializationId, open.IsActive)
                .ShouldBe(("2025-00001", client.PersonId, CaseStatus.InProgress, new DateOnly(2025, 3, 2), 150.00m, (Guid?)ids[("RoleSpecializations", 1)], true));
            open.Payments.Select(payment => (payment.Amount, payment.Note)).ShouldBe([(150.00m, Case.LegacyPaymentNote)]);
            open.History.Select(change => change.ToStatus).ShouldBe([CaseStatus.InProgress]);

            var completed = await db.Set<Case>().SingleAsync(row => row.Id == completedId, Ct);
            (completed.Number, completed.Status, completed.IsRejected, completed.IsActive, completed.ExpiresOn, completed.StartedOn)
                .ShouldBe(("2024-00001", CaseStatus.Completed, true, false, (DateOnly?)new DateOnly(2024, 7, 1), new DateOnly(2024, 6, 11)));
            completed.CompletedAt.ShouldBe(new DateTimeOffset(2024, 7, 1, 10, 0, 0, TimeSpan.Zero));
            completed.Payments.ShouldBeEmpty();

            (await db.Set<ClientProfile>().SingleAsync(profile => profile.Id == client.PersonId, Ct)).Status.ShouldBe(ClientStatus.Active);
        }

        var again = await ImportAsync(source, tenant);

        Result(again, "Subscriptions").ShouldBe((0, 0, 2));
        Result(again, "Memberships").ShouldBe((0, 2, 0));
        await using (var db = Context(tenant))
        {
            (await db.Set<Case>().CountAsync(Ct)).ShouldBe(2);
            (await db.Set<ServiceFolder>().CountAsync(Ct)).ShouldBe(2);
            (await db.Database.SqlQuery<int>($"SELECT last_number AS \"Value\" FROM cases.case_numbers WHERE year = 2025").SingleAsync(Ct)).ShouldBe(1);
        }
    }

    private static (int Created, int Updated, int Skipped) Result(LegacyImportReport report, string table)
    {
        var result = report.Tables[table];
        return (result.Created, result.Updated, result.Skipped);
    }

    private static async Task<LegacyImportReport> ImportAsync(LegacySource source, NpgsqlDataSource tenant)
    {
        var hasher = Substitute.For<IPasswordHasher>();
        hasher.Verify(Arg.Any<string>(), Arg.Any<PasswordFormat>(), Arg.Any<string>()).Returns(PasswordVerification.Failed);
        var importer = new LegacyImporter(hasher, Substitute.For<IImageProcessor>());
        await using var db = Context(tenant);
        return await importer.RunAsync(source, db, TimeProvider.System, Rome, "it", dryRun: false, Ct);
    }

    private static TenantDbContext Context(NpgsqlDataSource tenant)
    {
        var system = Substitute.For<ICurrentUser>();
        system.ActorType.Returns(ActorType.System);
        return new TenantDbContext(TenantDbContextOptions.Create(tenant, new TenantAuditInterceptor(system, TimeProvider.System)));
    }
}
