using Auxilia.MigrationRunner.LegacyImport;
using Auxilia.MigrationRunner.LegacyImport.Steps;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.LegacyImport.Tests;

/// <summary>The value rules of E-02 (docs/migration/mapping.md §5.1, §6), without databases.</summary>
public sealed class UsersMappingTests
{
    private static readonly DateOnly Today = new(2026, 10, 6);

    [Fact]
    public void Roles_DropSystemConfigurator_AndReturnUnknownNames()
    {
        UsersStep.Roles(["Client", "SystemConfigurator", "Trainer"], out var unknown).ShouldBe([TenantRole.Client]);
        unknown.ShouldBe(["Trainer"]);
        UsersStep.Roles(["SystemConfigurator"], out _).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Mario", "Verdi", "Mario", "Verdi")]
    [InlineData("Anna Maria Neri", "", "Anna Maria", "Neri")]
    [InlineData("Administrator", "", "Administrator", "Administrator")]
    public void Details_TakeTheNamesFromFullNameAndSurname(string fullName, string surname, string first, string last)
    {
        var (details, dropped) = UsersStep.Details(User(fullName, surname), birthDate: null, Today);

        (details.FirstName, details.LastName).ShouldBe((first, last));
        dropped.ShouldBeEmpty();
    }

    [Fact]
    public void Details_LeaveOutInvalidOptionalValues()
    {
        var row = User("Mario", "Verdi", email: "not-an-email", phone: "abc", fiscalCode: "XYZ");

        var (details, dropped) = UsersStep.Details(row, new DateOnly(1890, 1, 1), Today);

        (details.Email, details.Phone, details.FiscalCode, details.BirthDate).ShouldBe((null, null, null, (DateOnly?)null));
        dropped.ShouldBe(["email", "phone", "fiscalCode", "birthDate"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("mario.rossi", "mario.rossi")]
    [InlineData(" mario  rossi ", "mario.rossi")]
    public void UserName_HasNoWhiteSpace(string legacy, string expected) => UsersStep.UserName(legacy).ShouldBe(expected);

    [Theory]
    [InlineData("Password", "password")]
    [InlineData("Otp", "email-otp")]
    [InlineData("Sso", null)]
    public void Method_MapsTheLegacyLoginType(string legacy, string? expected) => AccountSecurityStep.Method(legacy).ShouldBe(expected);

    [Fact]
    public void LocalDate_ReadsCalendarDatesInTheTenantZone()
    {
        var rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
        var context = new LegacyImportContext(null!, null!, null!, new LegacyImportReport(), TimeProvider.System, rome, "it");

        context.LocalDate(new DateTime(1980, 5, 9, 22, 0, 0, DateTimeKind.Utc)).ShouldBe(new DateOnly(1980, 5, 10));
        context.LocalDate(new DateTime(1980, 1, 9, 23, 0, 0, DateTimeKind.Utc)).ShouldBe(new DateOnly(1980, 1, 10));
        context.LocalDate(DateTime.MinValue).ShouldBeNull();
    }

    [Fact]
    public void Report_GroupsIssuesWithoutPersonalData()
    {
        var report = new LegacyImportReport();
        report.For("Users").Created = 2;
        report.Warn("Users", 7, "phone not valid: left out");
        report.Warn("Users", 9, "phone not valid: left out");
        report.Skip("Users", 11, "the user has no role");

        var text = report.Render(dryRun: false);

        report.Tables["Users"].Skipped.ShouldBe(1);
        text.ShouldContain("warning Users: phone not valid: left out (2: legacy ids 7, 9)");
        text.ShouldContain("skipped Users: the user has no role (1: legacy ids 11)");
    }

    private static LegacyUser User(string fullName, string surname, string email = "mario@example.test", string? phone = null, string? fiscalCode = null) => new()
    {
        Id = 1,
        Username = "mario",
        PasswordHash = "hash",
        FullName = fullName,
        Surname = surname,
        Email = email,
        Phone = phone,
        FiscalCode = fiscalCode,
    };
}
