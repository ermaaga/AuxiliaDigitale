using Auxilia.Application.Identity;
using Auxilia.Application.Tests.Execution;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Tests.Identity;

/// <summary>N04: authenticator app of the tenant users (sign-in, enrolment, disable, reset) and "stay signed in".</summary>
public sealed class TwoFactorTests : IAsyncDisposable
{
    private const string Password = "Initial!Pass1";

    private readonly InMemoryIdentityData identity = new();
    private readonly InMemorySessionData sessions;
    private readonly InMemoryClientStore clients = new();
    private readonly FakeHasher hasher = new();
    private readonly ManualTimeProvider time = new();
    private readonly ConfigurableSettings settings = new();
    private readonly InMemoryDenyList denyList = new();
    private readonly RecordingRealtimeNotifier realtime = new();
    private readonly RecordingDispatcher dispatcher = new();
    private readonly RecordingLogger<SessionManager> log = new();
    private readonly SessionManager manager;
    private readonly AccountLinkManager links;

    public TwoFactorTests()
    {
        sessions = new InMemorySessionData(identity);
        clients.Add(ClientApplication.Create(Guid.CreateVersion7(), "mobile", "Mobile", ClientApplicationType.Mobile).Value);
        var policy = new PasswordPolicy(settings, hasher);
        var runner = Platform.ManagerHarness.Runner();
        var authenticator = new PasswordAuthenticator(runner, identity, hasher, settings, time, new RecordingLogger<PasswordAuthenticator>());
        manager = new SessionManager(
            runner, sessions, authenticator, new ClientApplicationValidator(clients, hasher, new RecordingLogger<ClientApplicationValidator>()),
            new FakeAccessTokenIssuer(time), denyList, settings, SessionSettings.Tenant(), realtime, policy, hasher,
            new FakeUserTotp(time), new PrefixUserProtector(), new FixedAppName(), time, log);
        links = new AccountLinkManager(
            runner, sessions, hasher, dispatcher, manager, settings, policy, SessionSettings.Tenant(), time, new RecordingLogger<AccountLinkManager>());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ClientCredentials Mobile => new("mobile", null);

    public async ValueTask DisposeAsync()
    {
        await sessions.DisposeAsync();
        await identity.DisposeAsync();
    }

    [Fact]
    public async Task Enrolment_ShowsTheSecretOnce_AndAConfirmedCodeEnablesTheApp()
    {
        var user = AddUser();

        (await manager.ConfirmEnrollmentAsync(user.Id, FakeUserTotp.Valid, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.TwoFactorEnrollmentMissing);
        var enrollment = (await manager.BeginEnrollmentAsync(user.Id, Ct)).Value;

        enrollment.Secret.ShouldBe("SECRET1");
        enrollment.Uri.ShouldBe("otpauth://totp/Studio Acme:mario.rossi?secret=SECRET1");
        user.PendingTwoFactorSecret.ShouldBe("protected:SECRET1");
        user.HasTwoFactor.ShouldBeFalse();
        (await manager.ConfirmEnrollmentAsync(user.Id, "999999", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.TwoFactorCodeRejected);

        (await manager.ConfirmEnrollmentAsync(user.Id, FakeUserTotp.Valid, Ct)).IsSuccess.ShouldBeTrue();

        (user.TwoFactorSecret, user.PendingTwoFactorSecret, user.TwoFactorEnabledAt).ShouldBe(("protected:SECRET1", null, time.GetUtcNow()));
        (await manager.StatusAsync(user.Id, Ct)).Value.ShouldBe(new TwoFactorStatus(true, false, time.GetUtcNow()));
        (await manager.BeginEnrollmentAsync(user.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.TwoFactorAlreadyEnabled);
        log.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Security.TwoFactorChanged);
    }

    [Fact]
    public async Task SignIn_WithTheApp_AsksForTheCode_RefusesWrongAndReplayedCodes()
    {
        var user = await AddUserWithAppAsync();

        (await manager.SignInAsync(SignIn(), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.TwoFactorRequired);
        user.AccessFailedCount.ShouldBe(0);
        (await manager.SignInAsync(SignIn(code: "999999"), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.TwoFactorCodeRejected);
        user.AccessFailedCount.ShouldBe(1);
        sessions.Sessions.ShouldBeEmpty();

        (await manager.SignInAsync(SignIn(code: FakeUserTotp.Valid), Ct)).IsSuccess.ShouldBeTrue();
        user.AccessFailedCount.ShouldBe(0);

        // The same code (same step) again: a replay.
        (await manager.SignInAsync(SignIn(code: FakeUserTotp.Valid), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.TwoFactorCodeRejected);
        sessions.LoginAttempts.Select(attempt => (attempt.Succeeded, attempt.FailureReason)).ShouldBe(
            [(false, "TwoFactorRequired"), (false, "TwoFactorCodeRejected"), (true, null), (false, "TwoFactorCodeRejected")]);
        log.Entries.Count(entry => entry.EventId.Id == EventCodes.Security.TwoFactorCodeFailed).ShouldBe(2);
    }

    [Fact]
    public async Task SignIn_TheRightPasswordDoesNotResetTheCounter_SoWrongCodesLockTheAccount()
    {
        var user = await AddUserWithAppAsync();
        var maxAttempts = IdentitySettings.LockoutMaxFailedAttempts.Default;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            await manager.SignInAsync(SignIn(code: "999999"), Ct);
        }

        user.IsLockedOut(time.GetUtcNow()).ShouldBeTrue();
        (await manager.SignInAsync(SignIn(code: FakeUserTotp.Valid), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.AccountLocked);
    }

    [Fact]
    public async Task RequiredByRole_LeadsToTheSetup_ThatSignsInAfterAValidCode()
    {
        var user = AddUser();
        settings.Values["auth.mfa.requiredRoles"] = "Administrator, Employee";

        (await manager.SignInAsync(SignIn(), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.TwoFactorSetupRequired);
        (await manager.BeginRequiredSetupAsync(new TwoFactorSetup(Mobile, "mario.rossi", "wrong", null, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.InvalidCredentials);
        var enrollment = (await manager.BeginRequiredSetupAsync(new TwoFactorSetup(Mobile, "mario.rossi", Password, null, null), Ct)).Value;
        enrollment.Secret.ShouldBe("SECRET1");

        (await manager.ConfirmRequiredSetupAsync(Confirmation("999999"), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.TwoFactorCodeRejected);
        var pair = (await manager.ConfirmRequiredSetupAsync(Confirmation(FakeUserTotp.Valid), Ct)).Value;

        pair.SessionId.ShouldNotBe(Guid.Empty);
        user.HasTwoFactor.ShouldBeTrue();
        (await manager.StatusAsync(user.Id, Ct)).Value.Required.ShouldBeTrue();
        (await manager.DisableAsync(user.Id, Password, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.TwoFactorRequiredByRole);
        (await manager.BeginRequiredSetupAsync(new TwoFactorSetup(Mobile, "mario.rossi", Password, null, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.TwoFactorAlreadyEnabled);
    }

    [Fact]
    public async Task RequiredSetup_IsRefused_WhenTheRolesDoNotRequireTheApp()
    {
        AddUser();
        settings.Values["auth.mfa.requiredRoles"] = "Administrator";

        (await manager.SignInAsync(SignIn(), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.BeginRequiredSetupAsync(new TwoFactorSetup(Mobile, "mario.rossi", Password, null, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.TwoFactorEnrollmentMissing);
    }

    [Fact]
    public async Task Disable_NeedsTheCurrentPassword()
    {
        var user = await AddUserWithAppAsync();

        (await manager.DisableAsync(user.Id, "wrong", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.CurrentPasswordInvalid);
        user.AccessFailedCount.ShouldBe(1);
        user.HasTwoFactor.ShouldBeTrue();

        (await manager.DisableAsync(user.Id, Password, Ct)).IsSuccess.ShouldBeTrue();
        user.HasTwoFactor.ShouldBeFalse();
        (await manager.SignInAsync(SignIn(), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Reset_RemovesTheApp_AndEndsTheSessionsOfTheUser()
    {
        var user = await AddUserWithAppAsync();
        var pair = (await manager.SignInAsync(SignIn(code: FakeUserTotp.Valid), Ct)).Value;
        var administrator = Guid.CreateVersion7();

        (await manager.ResetAsync(Guid.CreateVersion7(), administrator, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        (await manager.ResetAsync(user.Id, administrator, Ct)).IsSuccess.ShouldBeTrue();

        user.HasTwoFactor.ShouldBeFalse();
        sessions.Sessions.ShouldHaveSingleItem().EndReason.ShouldBe(SessionEndReason.SecurityStampChanged);
        denyList.Sessions.ShouldContainKey(pair.SessionId);
        realtime.Pushes.ShouldHaveSingleItem().Payload.ShouldBe(new ForceLogoutEvent("SecurityStampChanged"));
        var changed = log.Entries.Last(entry => entry.EventId.Id == EventCodes.Security.TwoFactorChanged);
        (changed.Properties["Change"], changed.Properties["ActorType"], changed.Properties["ActorId"]).ShouldBe(("Reset", "Administrator", (object?)administrator));
    }

    [Fact]
    public async Task ResetByOperator_FindsTheUserByName()
    {
        var user = await AddUserWithAppAsync();

        (await manager.ResetByOperatorAsync("nobody", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        (await manager.ResetByOperatorAsync(" MARIO.ROSSI ", Ct)).Value.ShouldBe("mario.rossi");
        user.HasTwoFactor.ShouldBeFalse();
    }

    [Fact]
    public async Task EmailOtp_AsksForTheAppCode_WithoutUsingTheEmailedCode()
    {
        await AddUserWithAppAsync();
        settings.Values["auth.otp.enabled"] = true;
        await links.SendLoginOtpAsync("mario.rossi", Ct);
        var code = (string)dispatcher.Requests.ShouldHaveSingleItem().Model["code"]!;

        (await manager.SignInWithOtpAsync(new OtpSignIn(Mobile, "mario.rossi", code, null, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.TwoFactorRequired);
        (await manager.SignInWithOtpAsync(new OtpSignIn(Mobile, "mario.rossi", code, null, null, FakeUserTotp.Valid), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ExpiredPassword_WithTheApp_IsChangedOnlyWithItsCode()
    {
        var user = await AddUserWithAppAsync();
        settings.Values["auth.password.expiryEnabled"] = true;
        time.Advance(TimeSpan.FromDays(200));

        (await manager.ChangeExpiredPasswordAsync(new ExpiredPasswordChange(Mobile, "mario.rossi", Password, "Brand!New123", null, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.TwoFactorRequired);
        user.PasswordHash.ShouldBe("hash:" + Password);

        var changed = await manager.ChangeExpiredPasswordAsync(
            new ExpiredPasswordChange(Mobile, "mario.rossi", Password, "Brand!New123", null, null, FakeUserTotp.Valid), Ct);
        changed.IsSuccess.ShouldBeTrue();
        user.PasswordHash.ShouldBe("hash:Brand!New123");
    }

    [Fact]
    public async Task ExpiredPassword_OfARoleThatRequiresTheApp_IsChanged_ThenTheSetupIsRequired()
    {
        var user = AddUser();
        settings.Values["auth.password.expiryEnabled"] = true;
        settings.Values["auth.mfa.requiredRoles"] = "Employee";
        time.Advance(TimeSpan.FromDays(200));

        (await manager.ChangeExpiredPasswordAsync(new ExpiredPasswordChange(Mobile, "mario.rossi", Password, "Brand!New123", null, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.TwoFactorSetupRequired);
        user.PasswordHash.ShouldBe("hash:Brand!New123");
    }

    [Fact]
    public async Task RememberMe_KeepsTheSessionOpenForTheConfiguredDays_WithinTheAbsoluteLimit()
    {
        AddUser();
        settings.Values["auth.session.rememberMeDays"] = 7;

        var ordinary = (await manager.SignInAsync(SignIn(), Ct)).Value;
        var remembered = (await manager.SignInAsync(SignIn(rememberMe: true), Ct)).Value;

        ordinary.RememberedUntil.ShouldBeNull();
        remembered.RememberedUntil.ShouldBe(time.GetUtcNow().AddDays(7));
        var session = sessions.Sessions.Single(item => item.Id == remembered.SessionId);
        (session.IsRemembered, session.IdleExpiresAt).ShouldBe((true, time.GetUtcNow().AddDays(7)));

        // A refresh slides the window, never past the absolute limit (14 days by default).
        time.Advance(TimeSpan.FromDays(5));
        var refreshed = (await manager.RefreshAsync(new RefreshTokens(Mobile, remembered.RefreshToken, null, null), Ct)).Value;
        refreshed.RememberedUntil.ShouldBe(time.GetUtcNow().AddDays(7));
        time.Advance(TimeSpan.FromDays(5));
        var capped = (await manager.RefreshAsync(new RefreshTokens(Mobile, refreshed.RefreshToken, null, null), Ct)).Value;
        capped.RememberedUntil.ShouldBe(session.AbsoluteExpiresAt);
    }

    [Fact]
    public async Task RememberMe_IsIgnored_WhenTheTenantTurnedItOff()
    {
        AddUser();
        settings.Values["auth.session.rememberMeDays"] = 0;

        var pair = (await manager.SignInAsync(SignIn(rememberMe: true), Ct)).Value;

        pair.RememberedUntil.ShouldBeNull();
        sessions.Sessions.ShouldHaveSingleItem().IsRemembered.ShouldBeFalse();
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(" Employee , Administrator,", "Administrator,Employee")]
    [InlineData("employee", null)]
    [InlineData("Employee,Nobody", null)]
    public void RequiredRoles_AreACommaListOfTenantRoles(string value, string? expected)
    {
        var roles = IdentitySettings.ParseRoles(value);

        (roles is null ? null : string.Join(',', roles.Order())).ShouldBe(expected);
        IdentitySettings.MfaRequiredRoles.TryRead(System.Text.Json.JsonSerializer.Serialize(value), out _).ShouldBe(expected is not null);
    }

    private static PasswordSignIn SignIn(string? code = null, bool rememberMe = false) =>
        new(Mobile, "mario.rossi", Password, "10.0.0.1", "tests", code, rememberMe);

    private static TwoFactorSetupConfirmation Confirmation(string code) => new(Mobile, "mario.rossi", Password, code, false, null, null);

    private User AddUser()
    {
        var user = User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "mario.rossi", "mario@example.test", "it", [TenantRole.Employee], isActive: true).Value;
        user.SetPassword("hash:" + Password, PasswordFormat.Identity, time.GetUtcNow());
        identity.Users.Add(user);
        return user;
    }

    /// <summary>A user who enrolled the app; the clock moves to the next step, so sign-ins can use a fresh code.</summary>
    private async Task<User> AddUserWithAppAsync()
    {
        var user = AddUser();
        await manager.BeginEnrollmentAsync(user.Id, Ct);
        (await manager.ConfirmEnrollmentAsync(user.Id, FakeUserTotp.Valid, Ct)).IsSuccess.ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(30));
        return user;
    }
}
