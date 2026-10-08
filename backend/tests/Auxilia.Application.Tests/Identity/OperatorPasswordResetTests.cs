using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Identity;
using Auxilia.Application.Messaging.Public;
using Auxilia.Application.Tests.Execution;
using Auxilia.Contracts.Identity;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Tests.Identity;

/// <summary>F31: <c>auxctl users reset-password</c> — temporary password with forced change, or a reset link.</summary>
public sealed class OperatorPasswordResetTests : IAsyncDisposable
{
    private const string Password = "Initial!Pass1";

    private readonly InMemoryIdentityData identity = new();
    private readonly InMemorySessionData sessions;
    private readonly InMemoryClientStore clients = new();
    private readonly FakeHasher hasher = new();
    private readonly ManualTimeProvider time = new();
    private readonly ConfigurableSettings settings = new();
    private readonly RecordingDispatcher dispatcher = new();
    private readonly RecordingLogger<AccountLinkManager> log = new();
    private readonly SessionManager manager;
    private readonly AccountLinkManager links;

    public OperatorPasswordResetTests()
    {
        sessions = new InMemorySessionData(identity);
        clients.Add(ClientApplication.Create(Guid.CreateVersion7(), "mobile", "Mobile", ClientApplicationType.Mobile).Value);
        var policy = new PasswordPolicy(settings, hasher);
        var runner = Platform.ManagerHarness.Runner();
        var authenticator = new PasswordAuthenticator(runner, identity, hasher, settings, time, new RecordingLogger<PasswordAuthenticator>());
        manager = new SessionManager(
            runner, sessions, authenticator, new ClientApplicationValidator(clients, hasher, new RecordingLogger<ClientApplicationValidator>()),
            new FakeAccessTokenIssuer(time), new InMemoryDenyList(), settings, SessionSettings.Tenant(), new RecordingRealtimeNotifier(), policy, hasher,
            new FakeUserTotp(time), new PrefixUserProtector(), new FixedAppName(), time,
            new RecordingLogger<SessionManager>());
        links = new AccountLinkManager(runner, sessions, hasher, dispatcher, manager, settings, policy, SessionSettings.Tenant(), time, log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ClientCredentials Mobile => new("mobile", null);

    public async ValueTask DisposeAsync()
    {
        await sessions.DisposeAsync();
        await identity.DisposeAsync();
    }

    [Fact]
    public async Task TemporaryPassword_EndsTheSessionsAndMustBeChangedAtTheNextSignIn()
    {
        var user = AddUser("mario.rossi", "mario@example.test");
        (await manager.SignInAsync(SignIn(Password), Ct)).IsSuccess.ShouldBeTrue();

        var reset = (await links.ResetPasswordByOperatorAsync("MARIO.ROSSI", sendLink: false, Ct)).Value;

        (reset.UserId, reset.UserName).ShouldBe((user.Id, "mario.rossi"));
        var temporary = reset.TemporaryPassword.ShouldNotBeNull();
        user.PasswordHash.ShouldBe("hash:" + temporary);
        user.MustChangePassword.ShouldBeTrue();
        sessions.Sessions.ShouldAllBe(session => session.EndedAt != null);
        log.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Security.PasswordResetByOperator && entry.Level == LogLevel.Warning);
        dispatcher.Requests.ShouldBeEmpty();

        // Same flow as an expired password, even with expiry disabled.
        (await manager.SignInAsync(SignIn(temporary), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PasswordExpired);
        (await manager.ChangeExpiredPasswordAsync(new ExpiredPasswordChange(Mobile, "mario.rossi", temporary, "Chosen!Pass42", null, null), Ct))
            .IsSuccess.ShouldBeTrue();
        user.MustChangePassword.ShouldBeFalse();
        (await manager.SignInAsync(SignIn("Chosen!Pass42"), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task SendLink_EmailsAResetLinkAndKeepsThePassword()
    {
        var user = AddUser("mario.rossi", "mario@example.test");

        var reset = (await links.ResetPasswordByOperatorAsync("mario@example.test", sendLink: true, Ct)).Value;

        reset.TemporaryPassword.ShouldBeNull();
        dispatcher.Requests.ShouldHaveSingleItem().TemplateCode.ShouldBe(MessageTemplates.PasswordReset);
        user.PasswordHash.ShouldBe("hash:" + Password);
        user.MustChangePassword.ShouldBeFalse();
    }

    [Fact]
    public async Task UnknownAmbiguousOrWithoutEmail_Fail()
    {
        AddUser("anna", "shared@example.test");
        AddUser("bruno", "shared@example.test");
        AddUser("carla", null);

        (await links.ResetPasswordByOperatorAsync("nobody", false, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        (await links.ResetPasswordByOperatorAsync(" ", false, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        (await links.ResetPasswordByOperatorAsync("nobody@example.test", false, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        (await links.ResetPasswordByOperatorAsync("shared@example.test", false, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserAmbiguous);
        (await links.ResetPasswordByOperatorAsync("carla", true, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserEmailMissing);

        // The user name still works when the e-mail is shared.
        (await links.ResetPasswordByOperatorAsync("anna", false, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(8)]
    [InlineData(40)]
    public async Task TemporaryPassword_SatisfiesThePolicy(int minLength)
    {
        settings.Values["auth.password.minLength"] = minLength;
        var policy = new PasswordPolicy(settings, hasher);
        var rules = await policy.GetAsync(Ct);

        for (var index = 0; index < 50; index++)
        {
            var password = TemporaryPassword.Generate(rules);
            password.Length.ShouldBe(Math.Max(minLength, TemporaryPassword.MinLength));
            (await policy.ValidateAsync(null, password, Ct)).IsSuccess.ShouldBeTrue(password);
        }
    }

    [Fact]
    public void Generate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => TemporaryPassword.Generate(null!));

    [Fact]
    public async Task Policy_TreatsATemporaryPasswordAsExpired()
    {
        var user = AddUser("mario.rossi", null);
        var policy = new PasswordPolicy(settings, hasher);

        (await policy.IsExpiredAsync(user, time.GetUtcNow(), Ct)).ShouldBeFalse();
        user.SetTemporaryPassword("hash:Temp!Pass12345678", PasswordFormat.Identity, time.GetUtcNow());
        (await policy.IsExpiredAsync(user, time.GetUtcNow(), Ct)).ShouldBeTrue();
    }

    private static PasswordSignIn SignIn(string password) => new(Mobile, "mario.rossi", password, "10.0.0.1", "tests");

    private User AddUser(string userName, string? email)
    {
        var user = User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), userName, email, "it", [TenantRole.Client], isActive: true).Value;
        user.SetPassword("hash:" + Password, PasswordFormat.Identity, time.GetUtcNow());
        identity.Users.Add(user);
        return user;
    }
}
