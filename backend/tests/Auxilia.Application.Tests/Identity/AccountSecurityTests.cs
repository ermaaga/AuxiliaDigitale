using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Identity;
using Auxilia.Application.Messaging.Public;
using Auxilia.Application.Tests.Execution;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Identity;

/// <summary>F35: password policy and history, expiry, self-service change, e-mailed sign-in codes, login attempts.</summary>
public sealed class AccountSecurityTests : IAsyncDisposable
{
    private const string Password = "Initial!Pass1";

    private readonly InMemoryIdentityData identity = new();
    private readonly InMemorySessionData sessions;
    private readonly InMemoryClientStore clients = new();
    private readonly FakeHasher hasher = new();
    private readonly ManualTimeProvider time = new();
    private readonly ConfigurableSettings settings = new();
    private readonly FakeAccessTokenIssuer issuer;
    private readonly InMemoryDenyList denyList = new();
    private readonly RecordingRealtimeNotifier realtime = new();
    private readonly RecordingDispatcher dispatcher = new();
    private readonly RecordingLogger<SessionManager> log = new();
    private readonly PasswordPolicy policy;
    private readonly SessionManager manager;
    private readonly AccountLinkManager links;

    public AccountSecurityTests()
    {
        sessions = new InMemorySessionData(identity);
        issuer = new FakeAccessTokenIssuer(time);
        clients.Add(ClientApplication.Create(Guid.CreateVersion7(), "mobile", "Mobile", ClientApplicationType.Mobile).Value);
        policy = new PasswordPolicy(settings, hasher);

        var runner = Platform.ManagerHarness.Runner();
        var authenticator = new PasswordAuthenticator(runner, identity, hasher, settings, time, new RecordingLogger<PasswordAuthenticator>());
        manager = new SessionManager(
            runner, sessions, authenticator, new ClientApplicationValidator(clients, hasher, new RecordingLogger<ClientApplicationValidator>()), issuer, denyList,
            settings, SessionSettings.Tenant(), realtime, policy, hasher, time, log);
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

    [Theory]
    [InlineData("Short!1a", "validation.password.tooShort")]
    [InlineData("alllowercase!123", "validation.password.uppercase")]
    [InlineData("ALLUPPERCASE!123", "validation.password.lowercase")]
    [InlineData("NoDigitsHere!!", "validation.password.digit")]
    [InlineData("NoSpecial12345", "validation.password.special")]
    public async Task Policy_ReportsEveryBrokenRule(string password, string rule)
    {
        var result = await policy.ValidateAsync(null, password, Ct);

        result.Error!.Code.ShouldBe(EventCodes.Identity.PasswordTooWeak);
        result.Error.ValidationErrors["password"].ShouldBe([rule]);
        (await policy.ValidateAsync(null, "Good!Password1", Ct)).IsSuccess.ShouldBeTrue();
        (await policy.ValidateAsync(null, null!, Ct)).Error!.ValidationErrors["password"].Length.ShouldBe(5);
    }

    [Fact]
    public async Task Policy_RulesComeFromTheSettings()
    {
        settings.Values["auth.password.minLength"] = 8;
        settings.Values["auth.password.requireUppercase"] = false;
        settings.Values["auth.password.requireSpecial"] = false;

        (await policy.ValidateAsync(null, "lower123", Ct)).IsSuccess.ShouldBeTrue();
        var rules = await policy.GetAsync(Ct);
        (rules.MinLength, rules.RequireUppercase, rules.RequireLowercase, rules.HistoryCount).ShouldBe((8, false, true, 3));
    }

    [Fact]
    public async Task History_RejectsTheLastNPasswords_IncludingLegacyHashes()
    {
        var user = AddUser();
        time.Advance(TimeSpan.FromMinutes(1));
        user.SetPassword("bcrypt:Legacy!Pass123", PasswordFormat.LegacyBcrypt, time.GetUtcNow());
        time.Advance(TimeSpan.FromMinutes(1));
        foreach (var next in new[] { "Second!Pass12", "Third!Pass123" })
        {
            user.SetPassword("hash:" + next, PasswordFormat.Identity, time.GetUtcNow());
            time.Advance(TimeSpan.FromMinutes(1));
        }

        (await policy.ValidateAsync(user, "Third!Pass123", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PasswordReused);
        (await policy.ValidateAsync(user, "Legacy!Pass123", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PasswordReused);
        (await policy.ValidateAsync(user, Password, Ct)).IsSuccess.ShouldBeTrue("the 4th password back is outside the last 3");

        settings.Values["auth.password.historyCount"] = 0;
        (await policy.ValidateAsync(user, "Third!Pass123", Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task SignIn_RecordsEveryAttempt()
    {
        var user = AddUser();

        (await manager.SignInAsync(SignIn(), Ct)).IsSuccess.ShouldBeTrue();
        await manager.SignInAsync(SignIn(password: "wrong"), Ct);
        await manager.SignInAsync(SignIn(userName: "nobody"), Ct);
        await manager.SignInAsync(SignIn(client: new ClientCredentials("unknown", null)), Ct);

        sessions.LoginAttempts.Select(attempt => (attempt.UserId, attempt.Succeeded, attempt.FailureReason, attempt.Method)).ShouldBe(
        [
            (user.Id, true, null, "password"),
            (user.Id, false, "InvalidCredentials", "password"),
            ((Guid?)null, false, "InvalidCredentials", "password"),
            ((Guid?)null, false, "ClientInvalid", "password"),
        ]);
        sessions.LoginAttempts[0].IpAddress.ShouldBe("10.0.0.1");
        sessions.LoginAttempts[2].UserName.ShouldBe("nobody");
    }

    [Fact]
    public async Task ExpiredPassword_BlocksSignIn_UntilChangedWithTheCurrentOne()
    {
        var user = AddUser();
        settings.Values["auth.password.expiryEnabled"] = true;
        time.Advance(TimeSpan.FromDays(200));

        (await manager.SignInAsync(SignIn(), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PasswordExpired);
        sessions.LoginAttempts.Single().FailureReason.ShouldBe("PasswordExpired");

        (await manager.ChangeExpiredPasswordAsync(Change("wrong password", "Brand!New123"), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.InvalidCredentials);
        (await manager.ChangeExpiredPasswordAsync(Change(Password, Password), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PasswordReused);
        (await manager.ChangeExpiredPasswordAsync(Change(Password, "weak"), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PasswordTooWeak);
        var pair = (await manager.ChangeExpiredPasswordAsync(Change(Password, "Brand!New123"), Ct)).Value;

        pair.SessionId.ShouldNotBe(Guid.Empty);
        user.PasswordHash.ShouldBe("hash:Brand!New123");
        (await manager.SignInAsync(SignIn(password: "Brand!New123"), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Expiry_IsOffByDefault_AndCountsMonthsFromTheLastChange()
    {
        var user = AddUser();
        time.Advance(TimeSpan.FromDays(400));
        (await policy.IsExpiredAsync(user, time.GetUtcNow(), Ct)).ShouldBeFalse();

        settings.Values["auth.password.expiryEnabled"] = true;
        settings.Values["auth.password.expiryMonths"] = 24;
        (await policy.IsExpiredAsync(user, time.GetUtcNow(), Ct)).ShouldBeFalse();
        settings.Values["auth.password.expiryMonths"] = 12;
        (await policy.IsExpiredAsync(user, time.GetUtcNow(), Ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task ChangePassword_KeepsTheCallingSession_EndsTheOthers()
    {
        var user = AddUser();
        var current = (await manager.SignInAsync(SignIn(), Ct)).Value;
        var other = (await manager.SignInAsync(SignIn(), Ct)).Value;

        (await manager.ChangePasswordAsync(user.Id, current.SessionId, Password, "Changed!Pass1", Ct)).IsSuccess.ShouldBeTrue();

        user.PasswordHash.ShouldBe("hash:Changed!Pass1");
        var kept = sessions.Sessions.Single(session => session.Id == current.SessionId);
        (kept.EndedAt, kept.SecurityStamp).ShouldBe((null, user.SecurityStamp));
        sessions.Sessions.Single(session => session.Id == other.SessionId).EndReason.ShouldBe(SessionEndReason.SecurityStampChanged);
        realtime.Pushes.ShouldHaveSingleItem().ShouldBe(($"session:{other.SessionId}", RealtimeEvents.ForceLogout, (object)new ForceLogoutEvent("SecurityStampChanged")));
        (await manager.RefreshAsync(new RefreshTokens(Mobile, current.RefreshToken, null, null), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentCountsTowardTheLockout()
    {
        var user = AddUser();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await manager.ChangePasswordAsync(user.Id, null, "wrong", "Changed!Pass1", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.CurrentPasswordInvalid);
        }

        user.IsLockedOut(time.GetUtcNow()).ShouldBeTrue();
        (await manager.ChangePasswordAsync(user.Id, null, Password, "Changed!Pass1", Ct)).IsSuccess.ShouldBeTrue("a correct change clears the lockout");
        (await manager.ChangePasswordAsync(Guid.CreateVersion7(), null, Password, "Changed!Pass1", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        (await manager.ChangePasswordAsync(user.Id, null, "Changed!Pass1", "weak", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PasswordTooWeak);
    }

    [Fact]
    public async Task Otp_IsDisabledByDefault()
    {
        AddUser();

        (await links.SendLoginOtpAsync("mario.rossi", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.LoginMethodDisabled);
        (await manager.SignInWithOtpAsync(new OtpSignIn(Mobile, "mario.rossi", "123456", null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.LoginMethodDisabled);
        dispatcher.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Otp_SendsASingleUseCode_ThatSignsIn()
    {
        var user = AddUser();
        settings.Values["auth.otp.enabled"] = true;

        (await links.SendLoginOtpAsync(" MARIO.ROSSI ", Ct)).IsSuccess.ShouldBeTrue();
        (await links.SendLoginOtpAsync("nobody", Ct)).IsSuccess.ShouldBeTrue();

        var message = dispatcher.Requests.ShouldHaveSingleItem();
        message.TemplateCode.ShouldBe(MessageTemplates.LoginOtp);
        var code = (string)message.Model["code"]!;
        code.Length.ShouldBe(6);
        sessions.UserTokens.ShouldHaveSingleItem().ExpiresAt.ShouldBe(time.GetUtcNow().AddMinutes(10));

        var pair = (await manager.SignInWithOtpAsync(new OtpSignIn(Mobile, "mario.rossi", code, "10.0.0.2", null), Ct)).Value;
        pair.SessionId.ShouldNotBe(Guid.Empty);
        (await manager.SignInWithOtpAsync(new OtpSignIn(Mobile, "mario.rossi", code, null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.InvalidCredentials);
        sessions.LoginAttempts.Select(attempt => (attempt.Method, attempt.Succeeded, attempt.FailureReason)).ShouldBe(
            [("email-otp", true, null), ("email-otp", false, "InvalidOtp")]);
        user.AccessFailedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Otp_ExpiresAndWrongCodesLockTheAccount()
    {
        var user = AddUser();
        settings.Values["auth.otp.enabled"] = true;
        await links.SendLoginOtpAsync("mario.rossi", Ct);
        var code = (string)dispatcher.Requests.Single().Model["code"]!;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await manager.SignInWithOtpAsync(new OtpSignIn(Mobile, "mario.rossi", "000000" == code ? "111111" : "000000", null, null), Ct);
        }

        user.IsLockedOut(time.GetUtcNow()).ShouldBeTrue();
        sessions.UserTokens.ShouldAllBe(token => token.UsedAt != null);
        (await manager.SignInWithOtpAsync(new OtpSignIn(Mobile, "mario.rossi", code, null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.AccountLocked);
        (await links.SendLoginOtpAsync("mario.rossi", Ct)).IsSuccess.ShouldBeTrue();
        dispatcher.Requests.Count.ShouldBe(1, "no code for a locked account");

        time.Advance(TimeSpan.FromHours(1));
        await links.SendLoginOtpAsync("mario.rossi", Ct);
        var fresh = (string)dispatcher.Requests.Last().Model["code"]!;
        time.Advance(TimeSpan.FromMinutes(10));
        (await manager.SignInWithOtpAsync(new OtpSignIn(Mobile, "mario.rossi", fresh, null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.InvalidCredentials);
        (await manager.SignInWithOtpAsync(new OtpSignIn(Mobile, "nobody", fresh, null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.InvalidCredentials);
    }

    [Fact]
    public async Task Reset_EnforcesPolicyAndHistory()
    {
        var user = AddUser();
        await links.RequestPasswordResetAsync("mario.rossi", Ct);
        var token = System.Web.HttpUtility.ParseQueryString(new Uri((string)dispatcher.Requests.Single().Model["link"]!).Query)["token"]!;

        (await links.ResetPasswordAsync(token, Password, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PasswordReused);
        (await links.ResetPasswordAsync(token, "nouppercase!12", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PasswordTooWeak);
        (await links.ResetPasswordAsync(token, "Reset!Pass123", Ct)).IsSuccess.ShouldBeTrue();
        user.PasswordHistory.Count.ShouldBe(2);
    }

    [Fact]
    public async Task LoginAudit_ValidatesPagingAndListsTheEnabledMethods()
    {
        var reader = Substitute.For<ILoginAttemptReader>();
        var attempt = new LoginAttempt(Guid.CreateVersion7(), null, "nobody", "password", time.GetUtcNow(), false, "InvalidCredentials", "10.0.0.1", "ua");
        reader.ListAsync(Arg.Any<LoginAttemptQuery>(), Arg.Any<CancellationToken>()).Returns((new[] { attempt }, 41L));
        var audit = new LoginAuditQueryService([new PasswordAuthenticationMethod(), new EmailOtpAuthenticationMethod(settings)], reader);

        (await audit.GetMethodsAsync(Ct)).Methods.ShouldBe(["password"]);
        settings.Values["auth.otp.enabled"] = true;
        (await audit.GetMethodsAsync(Ct)).Methods.ShouldBe(["password", "email-otp"]);

        var page = (await audit.ListAttemptsAsync(new LoginAttemptQuery("nob", null, false, null, null, "-attemptedAt", 2, 20), Ct)).Value;
        (page.Page, page.PageSize, page.TotalCount).ShouldBe((2, 20, 41L));
        page.Items.ShouldHaveSingleItem().FailureReason.ShouldBe("InvalidCredentials");

        var invalid = await audit.ListAttemptsAsync(
            new LoginAttemptQuery(null, null, null, time.GetUtcNow(), time.GetUtcNow().AddDays(-1), "name", 0, 500), Ct);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["page", "pageSize", "sort", "from"], ignoreOrder: true);
    }

    private static PasswordSignIn SignIn(ClientCredentials? client = null, string userName = "mario.rossi", string password = Password) =>
        new(client ?? Mobile, userName, password, "10.0.0.1", "tests");

    private static ExpiredPasswordChange Change(string current, string next) => new(Mobile, "mario.rossi", current, next, null, null);

    private User AddUser()
    {
        var user = User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "mario.rossi", "mario@example.test", "it", [TenantRole.Employee], isActive: true).Value;
        user.SetPassword("hash:" + Password, PasswordFormat.Identity, time.GetUtcNow());
        identity.Users.Add(user);
        return user;
    }
}
