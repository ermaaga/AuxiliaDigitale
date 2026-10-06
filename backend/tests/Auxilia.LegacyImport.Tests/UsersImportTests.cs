using Auxilia.Application.Abstractions.Identity;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.MigrationRunner.LegacyImport;
using Auxilia.Persistence.Tenant.Operations;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using NSubstitute;

namespace Auxilia.LegacyImport.Tests;

/// <summary>E-02 end to end: legacy users, roles, specializations and account security into a new tenant.</summary>
[Collection(LegacyDatabaseGroup.Name)]
public sealed class UsersImportTests(LegacyDatabaseFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Import_UsersRolesAndSecurity_FollowTheMapping_AndRepeats()
    {
        await using var source = await LegacyAsync("legacy_users");
        await using var tenant = await fixture.CreateTenantAsync("tenant_users");

        var report = await ImportHarness.ImportAsync(source, tenant, hasher: new BcryptHasher());
        report.Reconciled.ShouldBeTrue(report.Render(dryRun: false));

        Result(report, "Users").ShouldBe((3, 0, 1, 1));
        Result(report, "RoleSpecializations").ShouldBe((1, 0, 0, 1));
        Result(report, "UserRoleSpecializations").ShouldBe((1, 0, 0, 1));
        Result(report, "PasswordHistories").Created.ShouldBe(1);
        Result(report, "LoginAuditLogs").ShouldBe((2, 0, 0, 1));
        report.Issues.ShouldContain(issue => issue.Table == "Users" && issue.LegacyId == 3 && issue.Reason == "phone not valid: left out");
        report.Issues.ShouldContain(issue => issue.LegacyId == 3 && issue.Reason.StartsWith("password assigned", StringComparison.Ordinal));
        report.Issues.ShouldContain(issue => issue.LegacyId == 3 && issue.Reason == "profile picture not readable: left out");
        report.Issues.ShouldContain(issue => issue.Table == "RoleSpecializations" && issue.Reason == "email not valid: left out");
        report.Render(dryRun: false).ShouldNotContain("Verdi");

        await using (var db = ImportHarness.Context(tenant))
        {
            var ids = await db.Set<LegacyIdMapping>().ToDictionaryAsync(row => (row.Entity, row.LegacyId), row => row.NewId, Ct);
            var users = await db.Set<User>().ToDictionaryAsync(user => user.Id, Ct);
            var admin = users[ids[("Users", 1)]];
            var employee = users[ids[("Users", 2)]];
            var client = users[ids[("Users", 3)]];
            ids.ContainsKey(("Users", 4)).ShouldBeFalse();

            admin.Roles.ShouldBe([TenantRole.Administrator]);
            admin.PasswordChangedAt.ShouldBe(new DateTimeOffset(2025, 4, 1, 8, 0, 0, TimeSpan.Zero));
            admin.MustChangePassword.ShouldBeFalse();
            (client.UserName, client.PasswordFormat, client.MustChangePassword, client.LanguageCode).ShouldBe(("mario.rossi", PasswordFormat.LegacyBcrypt, true, "it"));
            client.PasswordHistory.Select(entry => entry.PasswordHash).ShouldContain("older-hash");

            var person = await db.Set<Person>().SingleAsync(row => row.Id == client.PersonId, Ct);
            (person.FirstName, person.LastName, person.Phone, person.FiscalCode, person.BirthDate)
                .ShouldBe(("Mario", "Verdi", null, "VRDMRA80E10H501Z", (DateOnly?)new DateOnly(1980, 5, 10)));

            var profile = await db.Set<ClientProfile>().SingleAsync(row => row.Id == client.PersonId, Ct);
            profile.EmployeeUserId.ShouldBe(employee.Id);
            profile.Assignments.Single().AssignedAt.ShouldBe(new DateTimeOffset(2025, 2, 1, 10, 0, 0, TimeSpan.Zero));
            (await db.Set<Consent>().Where(row => row.PersonId == client.PersonId).Select(row => new { row.Purpose, row.Granted, row.Source }).ToListAsync(Ct))
                .ShouldBe([new { Purpose = ConsentPurpose.Marketing, Granted = true, Source = ConsentSource.LegacyMigration },
                    new { Purpose = ConsentPurpose.Privacy, Granted = true, Source = ConsentSource.LegacyMigration }], ignoreOrder: true);

            var employeeProfile = await db.Set<EmployeeProfile>().SingleAsync(Ct);
            (employeeProfile.Id, employeeProfile.IsDefault, employeeProfile.AdministratorUserId).ShouldBe((employee.Id, true, (Guid?)admin.Id));

            var specialization = await db.Set<Specialization>().SingleAsync(Ct);
            (specialization.Name, specialization.Role, specialization.IsPrivate, specialization.Email).ShouldBe(("Fiscale", TenantRole.Employee, true, null));
            specialization.Members.Select(member => member.UserId).ShouldBe([employee.Id]);

            (await db.Set<LoginAttempt>().OrderBy(row => row.AttemptedAt).Select(row => new { row.Method, row.UserId, row.Succeeded }).ToListAsync(Ct))
                .ShouldBe([new { Method = "password", UserId = (Guid?)admin.Id, Succeeded = true }, new { Method = "email-otp", UserId = (Guid?)null, Succeeded = false }]);
        }

        // A second run updates the same records: no new rows, same ids.
        var again = await ImportHarness.ImportAsync(source, tenant, hasher: new BcryptHasher());
        again.Reconciled.ShouldBeTrue(again.Render(dryRun: false));

        Result(again, "Users").ShouldBe((0, 3, 1, 1));
        Result(again, "RoleSpecializations").ShouldBe((0, 1, 0, 1));
        Result(again, "UserRoleSpecializations").Created.ShouldBe(0);
        Result(again, "PasswordHistories").Created.ShouldBe(0);
        Result(again, "LoginAuditLogs").Created.ShouldBe(0);
        await using (var db = ImportHarness.Context(tenant))
        {
            (await db.Set<User>().CountAsync(Ct)).ShouldBe(3);
            (await db.Set<Person>().CountAsync(Ct)).ShouldBe(3);
            (await db.Set<Consent>().CountAsync(Ct)).ShouldBe(2);
            (await db.Set<LoginAttempt>().CountAsync(Ct)).ShouldBe(2);
        }
    }

    [Fact]
    public async Task Import_DryRun_ReportsButSavesNothing()
    {
        await using var source = await LegacyAsync("legacy_users_dry");
        await using var tenant = await fixture.CreateTenantAsync("tenant_users_dry");

        var report = await ImportHarness.ImportAsync(source, tenant, dryRun: true, hasher: new BcryptHasher());
        report.Reconciled.ShouldBeTrue(report.Render(dryRun: false));

        Result(report, "Users").Created.ShouldBe(3);
        report.Render(dryRun: true).ShouldContain("dry run");
        await using var db = ImportHarness.Context(tenant);
        (await db.Set<User>().CountAsync(Ct)).ShouldBe(0);
        (await db.Set<LegacyIdMapping>().CountAsync(Ct)).ShouldBe(0);
    }

    private static (int Created, int Updated, int Excluded, int Skipped) Result(LegacyImportReport report, string table)
    {
        var result = report.Tables[table];
        return (result.Created, result.Updated, result.Excluded, result.Skipped);
    }

    private async Task<LegacySource> LegacyAsync(string name)
    {
        var connection = await fixture.CreateLegacyAsync(name, "legacy-security-update.sql", LegacyDatabaseFixture.Script("users.sql"));
        await using (var data = NpgsqlDataSource.Create(connection))
        {
            // The client got the password the legacy approval assigned; the others chose their own.
            await using var command = data.CreateCommand("""UPDATE "Users" SET "PasswordHash" = CASE WHEN "Id" = 3 THEN $1 ELSE $2 END""");
            command.Parameters.Add(new NpgsqlParameter { Value = BCrypt.Net.BCrypt.HashPassword("password", 4) });
            command.Parameters.Add(new NpgsqlParameter { Value = BCrypt.Net.BCrypt.HashPassword("chosen by the user", 4) });
            await command.ExecuteNonQueryAsync(Ct);
        }

        var opened = await LegacySource.OpenAsync(connection, Ct);
        opened.IsSuccess.ShouldBeTrue(opened.Error?.Description);
        return opened.Value;
    }

    /// <summary>The legacy verification of the composite hasher, without the Identity part.</summary>
    private sealed class BcryptHasher : IPasswordHasher
    {
        public string Hash(string password) => throw new NotSupportedException();

        public PasswordVerification Verify(string passwordHash, PasswordFormat format, string password)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, passwordHash) ? PasswordVerification.SuccessRehashNeeded : PasswordVerification.Failed;
            }
            catch (BCrypt.Net.SaltParseException)
            {
                return PasswordVerification.Failed;
            }
        }
    }
}
