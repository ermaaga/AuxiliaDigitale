using Auxilia.Application.Identity;
using Auxilia.Application.Tests.Execution;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Tests.Identity;

public sealed class IdentityServicesTests : IAsyncDisposable
{
    private readonly InMemoryIdentityData data = new();
    private readonly FakeHasher hasher = new();
    private readonly RecordingLogger<PasswordAuthenticator> authLog = new();
    private readonly RecordingLogger<UserAccountManager> accountLog = new();
    private readonly UserAccountManager accounts;
    private readonly PasswordAuthenticator authenticator;

    public IdentityServicesTests()
    {
        var settings = DefaultSettings.Create();
        accounts = new UserAccountManager(Platform.ManagerHarness.Runner(), data, hasher, settings, TimeProvider.System, accountLog);
        authenticator = new PasswordAuthenticator(Platform.ManagerHarness.Runner(), data, hasher, settings, TimeProvider.System, authLog);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_NeedsAnExistingPersonAndAFreeCaseInsensitiveUserName()
    {
        var person = Guid.CreateVersion7();

        (await accounts.CreateAsync(Request(person, "mario.rossi"), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PersonNotFound);

        data.People.Add(person);
        var created = await accounts.CreateAsync(Request(person, "mario.rossi"), Ct);
        created.IsSuccess.ShouldBeTrue();
        data.Users.ShouldHaveSingleItem().PasswordHash.ShouldBeNull();

        data.People.Add(Guid.CreateVersion7());
        (await accounts.CreateAsync(Request(data.People.Last(), "MARIO.ROSSI"), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNameTaken);
        (await accounts.CreateAsync(Request(person, "bad name"), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserValueInvalid);
    }

    [Fact]
    public async Task SetPassword_ChecksTheMinimumLengthAndLogsTheChange()
    {
        var user = await UserAsync();

        (await accounts.SetPasswordAsync(user.Id, "short", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PasswordTooWeak);
        (await accounts.SetPasswordAsync(user.Id, "a long enough pass", Ct)).IsSuccess.ShouldBeTrue();

        (user.PasswordHash, user.PasswordFormat).ShouldBe(("hash:a long enough pass", PasswordFormat.Identity));
        accountLog.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Security.PasswordChanged);
        (await accounts.SetPasswordAsync(Guid.CreateVersion7(), "a long enough pass", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
    }

    [Fact]
    public async Task RolesAndActivation_AreChangedAndLogged()
    {
        var user = await UserAsync();

        (await accounts.SetRolesAsync(user.Id, [TenantRole.Administrator], Ct)).IsSuccess.ShouldBeTrue();
        (await accounts.SetRolesAsync(user.Id, [], Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserValueInvalid);
        (await accounts.SetActiveAsync(user.Id, false, Ct)).IsSuccess.ShouldBeTrue();

        user.Roles.ShouldBe([TenantRole.Administrator]);
        user.IsActive.ShouldBeFalse();
        accountLog.Entries.Select(entry => entry.EventId.Id).ShouldContain(EventCodes.Security.RolesChanged);
        accountLog.Entries.Select(entry => entry.EventId.Id).ShouldContain(EventCodes.Security.AccountActivationChanged);
    }

    [Fact]
    public async Task Authenticate_UserNameIsCaseInsensitive()
    {
        var user = await UserAsync(password: "the right password");

        var result = await authenticator.AuthenticateAsync(" MARIO.ROSSI ", "the right password", Ct);

        result.Value.UserId.ShouldBe(user.Id);
        result.Value.Roles.ShouldBe([TenantRole.Employee]);
        user.LastLoginAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Authenticate_LegacyHashIsVerifiedThenUpgraded()
    {
        var user = await UserAsync();
        user.SetPassword("bcrypt:legacy secret", PasswordFormat.LegacyBcrypt, DateTimeOffset.UtcNow);

        (await authenticator.AuthenticateAsync("mario.rossi", "legacy secret", Ct)).IsSuccess.ShouldBeTrue();

        (user.PasswordHash, user.PasswordFormat).ShouldBe(("hash:legacy secret", PasswordFormat.Identity));
        authLog.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Security.LegacyPasswordUpgraded);
        (await authenticator.AuthenticateAsync("mario.rossi", "legacy secret", Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Authenticate_FailuresAreGenericAndLogTheReason()
    {
        var noPassword = await UserAsync("no.password");
        var inactive = await UserAsync("inactive", "the right password");
        inactive.SetActive(false);

        foreach (var (userName, password) in new[] { ("unknown", "x"), ("no.password", "x"), ("inactive", "the right password"), ("", "x") })
        {
            (await authenticator.AuthenticateAsync(userName, password, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.InvalidCredentials);
        }

        authLog.Entries.Where(entry => entry.EventId.Id == EventCodes.Security.LoginFailed).Select(entry => entry.Properties["Reason"])
            .ShouldBe(["UnknownUser", "NoPassword", "Inactive", "UnknownUser"]);
        authLog.Entries.ShouldAllBe(entry => !entry.Message.Contains("unknown", StringComparison.Ordinal));
        noPassword.AccessFailedCount.ShouldBe(1);
        inactive.AccessFailedCount.ShouldBe(0);
        hasher.Verifications.ShouldBe(3);
    }

    [Fact]
    public async Task Authenticate_LocksAfterTheConfiguredAttempts()
    {
        var user = await UserAsync(password: "the right password");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await authenticator.AuthenticateAsync("mario.rossi", "wrong", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.InvalidCredentials);
        }

        (await authenticator.AuthenticateAsync("mario.rossi", "the right password", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.AccountLocked);
        user.LockoutEnd.ShouldNotBeNull();
        authLog.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Security.AccountLockedOut && entry.Level == LogLevel.Warning);
    }

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private static CreateUser Request(Guid person, string userName) =>
        new(person, userName, "mario@example.test", "it", [TenantRole.Employee], IsActive: true);

    private async Task<User> UserAsync(string userName = "mario.rossi", string? password = null)
    {
        var person = Guid.CreateVersion7();
        data.People.Add(person);
        var id = (await accounts.CreateAsync(Request(person, userName), Ct)).Value;
        var user = data.Users.Single(item => item.Id == id);
        if (password is not null)
        {
            user.SetPassword("hash:" + password, PasswordFormat.Identity, DateTimeOffset.UtcNow);
        }

        return user;
    }
}
