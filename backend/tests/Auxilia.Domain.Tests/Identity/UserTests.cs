using Auxilia.Diagnostics;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Domain.Tests.Identity;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_NewAccountHasNoPasswordAndTrimmedValues()
    {
        var user = New(" mario.rossi ", " mario@example.test ");

        (user.UserName, user.Email, user.PasswordHash, user.IsActive, user.LanguageCode).ShouldBe(("mario.rossi", "mario@example.test", null, true, "it"));
        user.Roles.ShouldBe([TenantRole.Administrator, TenantRole.Employee]);
        user.SecurityStamp.Length.ShouldBe(User.SecurityStampLength);
    }

    [Theory]
    [InlineData("", "a@b.test", "it", "userName")]
    [InlineData("two words", "a@b.test", "it", "userName")]
    [InlineData("mario", "not-an-email", "it", "email")]
    [InlineData("mario", null, "", "languageCode")]
    public void Create_InvalidValues_AreRefused(string userName, string? email, string language, string field)
    {
        var result = User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), userName, email, language, [TenantRole.Client], isActive: true);

        result.Error!.Code.ShouldBe(EventCodes.Identity.UserValueInvalid);
        result.Error.ValidationErrors!.Keys.ShouldBe([field]);
    }

    [Fact]
    public void Create_WithoutRoles_IsRefused() =>
        User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "mario", null, "it", [], isActive: true).Error!.Code.ShouldBe(EventCodes.Identity.UserValueInvalid);

    [Fact]
    public void ImportLegacyPassword_KeepsTheLegacyHashItsDateAndHistory()
    {
        var user = New("mario", null);
        var stamp = user.SecurityStamp;
        var changed = Now.AddYears(-1);

        user.ImportLegacyPassword("$2a$11$legacy", changed, mustChange: true);

        (user.PasswordHash, user.PasswordFormat, user.PasswordChangedAt, user.MustChangePassword).ShouldBe(("$2a$11$legacy", PasswordFormat.LegacyBcrypt, (DateTimeOffset?)changed, true));
        user.SecurityStamp.ShouldNotBe(stamp);
        user.PasswordHistory.Select(entry => (entry.PasswordHash, entry.Format, entry.CreatedAt)).ShouldBe([("$2a$11$legacy", PasswordFormat.LegacyBcrypt, changed)]);

        // A second import of the same hash changes nothing but the flags; older hashes join the history once.
        stamp = user.SecurityStamp;
        user.ImportLegacyPassword("$2a$11$legacy", changed, mustChange: false);
        user.SecurityStamp.ShouldBe(stamp);
        user.MustChangePassword.ShouldBeFalse();
        user.ImportLegacyPasswordHistory("$2a$11$older", changed.AddMonths(-6)).ShouldBeTrue();
        user.ImportLegacyPasswordHistory("$2a$11$older", changed.AddMonths(-6)).ShouldBeFalse();
        user.PasswordHistory.Select(entry => entry.PasswordHash).ShouldBe(["$2a$11$legacy", "$2a$11$older"]);
    }

    [Fact]
    public void ImportLegacyPasswordHistory_KeepsTheNewestEntries()
    {
        var user = New("mario", null);

        for (var index = 0; index < User.MaxPasswordHistory + 2; index++)
        {
            user.ImportLegacyPasswordHistory($"hash-{index}", Now.AddDays(index));
        }

        user.PasswordHistory.Count.ShouldBe(User.MaxPasswordHistory);
        user.PasswordHistory[^1].PasswordHash.ShouldBe("hash-2");
    }

    [Fact]
    public void FailedSignIns_LockProgressivelyAndSuccessResets()
    {
        var user = New();

        for (var attempt = 1; attempt < 3; attempt++)
        {
            user.RecordFailedSignIn(Now, maxFailedAttempts: 3, TimeSpan.FromMinutes(5)).ShouldBeNull();
        }

        user.RecordFailedSignIn(Now, 3, TimeSpan.FromMinutes(5)).ShouldBe(Now.AddMinutes(5));
        user.IsLockedOut(Now.AddMinutes(4)).ShouldBeTrue();
        user.IsLockedOut(Now.AddMinutes(5)).ShouldBeFalse();

        user.RecordFailedSignIn(Now, 3, TimeSpan.FromMinutes(5));
        user.RecordFailedSignIn(Now, 3, TimeSpan.FromMinutes(5));
        user.RecordFailedSignIn(Now, 3, TimeSpan.FromMinutes(5)).ShouldBe(Now.AddMinutes(10));

        for (var lockouts = 0; lockouts < 12 * 3; lockouts++)
        {
            user.RecordFailedSignIn(Now, 3, TimeSpan.FromMinutes(5));
        }

        user.LockoutEnd.ShouldBe(Now + User.MaxLockout);

        user.RecordSuccessfulSignIn(Now);
        (user.AccessFailedCount, user.LockoutCount, user.LockoutEnd, user.LastLoginAt).ShouldBe((0, 0, null, Now));
    }

    [Fact]
    public void Password_SetUnlocksAndChangesTheStamp_UpgradeKeepsIt()
    {
        var user = New();
        user.RecordFailedSignIn(Now, 1, TimeSpan.FromMinutes(5));
        var stamp = user.SecurityStamp;

        user.SetPassword("legacy", PasswordFormat.LegacyBcrypt, Now);
        (user.PasswordHash, user.PasswordFormat, user.PasswordChangedAt, user.LockoutEnd).ShouldBe(("legacy", PasswordFormat.LegacyBcrypt, Now, null));
        user.SecurityStamp.ShouldNotBe(stamp);

        stamp = user.SecurityStamp;
        user.UpgradePasswordHash("pbkdf2");
        (user.PasswordHash, user.PasswordFormat, user.SecurityStamp).ShouldBe(("pbkdf2", PasswordFormat.Identity, stamp));
    }

    [Fact]
    public void RolesAndActivation_ChangeTheStamp()
    {
        var user = New();
        var stamp = user.SecurityStamp;

        user.SetRoles([TenantRole.Client, TenantRole.Client]).IsSuccess.ShouldBeTrue();
        user.Roles.ShouldBe([TenantRole.Client]);
        user.SecurityStamp.ShouldNotBe(stamp);
        user.SetRoles([]).Error!.Code.ShouldBe(EventCodes.Identity.UserValueInvalid);

        stamp = user.SecurityStamp;
        user.SetActive(true);
        user.SecurityStamp.ShouldBe(stamp);
        user.SetActive(false);
        (user.IsActive, user.SecurityStamp == stamp).ShouldBe((false, false));
    }

    [Fact]
    public void Person_TrimsAndValidates()
    {
        var person = new Person(Guid.CreateVersion7(), " Mario ", " Rossi ", " ");

        (person.FirstName, person.LastName, person.Email).ShouldBe(("Mario", "Rossi", null));
        Should.Throw<ArgumentException>(() => new Person(Guid.CreateVersion7(), "", "Rossi", null));
        Should.Throw<ArgumentOutOfRangeException>(() => new Person(Guid.CreateVersion7(), "Mario", new string('r', 101), null));
    }

    [Fact]
    public void ChangeLanguageAndTheme_ValidateTheValue()
    {
        var user = New();

        user.Theme.ShouldBe(UserTheme.System);
        user.ChangeLanguage("en").IsSuccess.ShouldBeTrue();
        user.ChangeLanguage(" ").Error!.ValidationErrors.Keys.ShouldBe(["languageCode"]);
        user.ChangeLanguage("a-very-long-code").Error!.Code.ShouldBe(EventCodes.Identity.UserValueInvalid);
        user.ChangeTheme(UserTheme.Dark).IsSuccess.ShouldBeTrue();
        user.ChangeTheme((UserTheme)42).Error!.ValidationErrors.Keys.ShouldBe(["theme"]);
        (user.LanguageCode, user.Theme).ShouldBe(("en", UserTheme.Dark));
    }

    [Fact]
    public void UserImage_KeepsTheHashOfItsContent()
    {
        var userId = Guid.CreateVersion7();

        var image = UserImage.Create(userId, [1, 2, 3], "image/jpeg");
        var first = image.Hash;
        image.Replace([4, 5], "image/jpeg");

        (image.Id, first.Length).ShouldBe((userId, UserImage.HashLength));
        image.Content.ShouldBe(new byte[] { 4, 5 });
        image.Hash.ShouldNotBe(first);
        image.Hash.ShouldBe(UserImage.Create(userId, [4, 5], "image/jpeg").Hash);
        Should.Throw<ArgumentOutOfRangeException>(() => UserImage.Create(userId, [], "image/jpeg"));
    }

    private static User New(string userName = "mario", string? email = null) =>
        User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), userName, email, "it", [TenantRole.Employee, TenantRole.Administrator], isActive: true).Value;

    [Fact]
    public void ChangeAccount_NewUserName_ChangesTheStamp_AndValidates()
    {
        var user = User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "mario@example.test", "mario@example.test", "it", [TenantRole.Client], isActive: true).Value;
        var stamp = user.SecurityStamp;

        user.ChangeAccount(" MARIO@example.test ", "mario@example.test").IsSuccess.ShouldBeTrue();
        user.SecurityStamp.ShouldBe(stamp);
        user.ChangeAccount("mario.rossi", "m.rossi@example.test").IsSuccess.ShouldBeTrue();
        (user.UserName, user.Email).ShouldBe(("mario.rossi", "m.rossi@example.test"));
        user.SecurityStamp.ShouldNotBe(stamp);

        user.ChangeAccount("with space", null).Error!.Code.ShouldBe(EventCodes.Identity.UserValueInvalid);
        user.ChangeAccount("mario.rossi", "no-at").IsFailure.ShouldBeTrue();
        user.UserName.ShouldBe("mario.rossi");
    }
}
