using System.Web;

using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Identity;
using Auxilia.Application.Messaging.Public;
using Auxilia.Application.Tests.Execution;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Messaging;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using NSubstitute;

namespace Auxilia.Application.Tests.Identity;

public sealed class AccountLinkManagerTests : IAsyncDisposable
{
    private readonly InMemoryIdentityData identity = new();
    private readonly InMemorySessionData sessions;
    private readonly FakeHasher hasher = new();
    private readonly ManualTimeProvider time = new();
    private readonly RecordingDispatcher dispatcher = new();
    private readonly ISessionManager sessionManager = Substitute.For<ISessionManager>();
    private readonly RecordingLogger<AccountLinkManager> log = new();
    private readonly AccountLinkManager links;

    public AccountLinkManagerTests()
    {
        sessions = new InMemorySessionData(identity);
        sessionManager.EndSessionAsync(Arg.Any<Guid>(), Arg.Any<SessionEndReason>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        links = new AccountLinkManager(
            Platform.ManagerHarness.Runner(), sessions, hasher, dispatcher, sessionManager, SessionSettings.Create(), SessionSettings.Tenant(), time, log);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await sessions.DisposeAsync();
        await identity.DisposeAsync();
    }

    [Fact]
    public async Task SendActivation_QueuesALinkWithAOneUseToken()
    {
        var user = AddUser(password: null);

        (await links.SendActivationAsync(user.Id, Ct)).IsSuccess.ShouldBeTrue();

        var message = dispatcher.Requests.ShouldHaveSingleItem();
        (message.Channel, message.Purpose, message.Recipient, message.TemplateCode, message.Language)
            .ShouldBe((MessageChannel.Email, MessagePurpose.Transactional, "mario@example.test", MessageTemplates.AccountActivation, "it"));
        var link = (string)message.Model["link"]!;
        link.ShouldStartWith("http://localhost:3000/acme/activate?token=");
        var stored = sessions.UserTokens.ShouldHaveSingleItem();
        stored.TokenHash.ShouldBe(SecureTokens.Hash(TokenOf(link)));
        stored.ExpiresAt.ShouldBe(time.GetUtcNow() + TimeSpan.FromHours(IdentitySettings.ActivationLinkHours.Default));
        stored.TokenHash.ShouldNotBe(TokenOf(link));
    }

    [Fact]
    public async Task SendActivation_NeedsAUserWithAnEmail_AndANewLinkReplacesTheOldOne()
    {
        var withoutEmail = User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "no.mail", null, "it", [TenantRole.Client], isActive: true).Value;
        identity.Users.Add(withoutEmail);
        var user = AddUser(password: null);

        (await links.SendActivationAsync(Guid.CreateVersion7(), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        (await links.SendActivationAsync(withoutEmail.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserEmailMissing);

        await links.SendActivationAsync(user.Id, Ct);
        await links.SendActivationAsync(user.Id, Ct);

        var first = TokenOf((string)dispatcher.Requests[0].Model["link"]!);
        (await links.ActivateAsync(first, "a long enough pass", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserTokenInvalid);
    }

    [Fact]
    public async Task Activate_SetsThePasswordOnce()
    {
        var user = AddUser(password: null);
        await links.SendActivationAsync(user.Id, Ct);
        var token = TokenOf((string)dispatcher.Requests.Single().Model["link"]!);

        (await links.ActivateAsync(token, "short", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PasswordTooWeak);
        (await links.ActivateAsync(token, "a long enough pass", Ct)).IsSuccess.ShouldBeTrue();

        user.PasswordHash.ShouldBe("hash:a long enough pass");
        log.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Security.AccountActivated);
        (await links.ActivateAsync(token, "a long enough pass", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserTokenInvalid);
        (await links.ActivateAsync("", "a long enough pass", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserTokenInvalid);
    }

    [Fact]
    public async Task Activate_AfterTheLinkExpired_Fails()
    {
        var user = AddUser(password: null);
        await links.SendActivationAsync(user.Id, Ct);
        time.Advance(TimeSpan.FromHours(IdentitySettings.ActivationLinkHours.Default));

        (await links.ActivateAsync(TokenOf((string)dispatcher.Requests.Single().Model["link"]!), "a long enough pass", Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.UserTokenInvalid);
        user.PasswordHash.ShouldBeNull();
    }

    [Fact]
    public async Task RequestPasswordReset_AlwaysSucceeds_ButSendsOnlyToActiveUsersWithAPassword()
    {
        AddUser("no.password", password: null);
        AddUser("inactive", "old password").SetActive(false);
        AddUser("mario.rossi", "old password");

        foreach (var userName in new[] { "unknown", "no.password", "inactive", "", " MARIO.ROSSI " })
        {
            (await links.RequestPasswordResetAsync(userName, Ct)).IsSuccess.ShouldBeTrue();
        }

        var message = dispatcher.Requests.ShouldHaveSingleItem();
        message.TemplateCode.ShouldBe(MessageTemplates.PasswordReset);
        ((string)message.Model["link"]!).ShouldStartWith("http://localhost:3000/acme/reset-password?token=");
        log.Entries.Count(entry => entry.EventId.Id == EventCodes.Security.PasswordResetRequested).ShouldBe(5);
        log.Entries.ShouldAllBe(entry => !entry.Message.Contains("unknown", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RequestPasswordReset_HidesADeliveryFailure()
    {
        AddUser("mario.rossi", "old password");
        dispatcher.Outcome = Errors.Identity.UserEmailMissing();

        (await links.RequestPasswordResetAsync("mario.rossi", Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task ResetPassword_SetsTheNewPasswordAndEndsTheOpenSessions()
    {
        var user = AddUser("mario.rossi", "old password");
        var open = new RefreshSession(Guid.CreateVersion7(), user.Id, "web", user.SecurityStamp, time.GetUtcNow(), time.GetUtcNow().AddHours(1), time.GetUtcNow().AddDays(1), null, null);
        sessions.Add(open);
        await links.RequestPasswordResetAsync("mario.rossi", Ct);
        var token = TokenOf((string)dispatcher.Requests.Single().Model["link"]!);

        (await links.ResetPasswordAsync(token, "a new long password", Ct)).IsSuccess.ShouldBeTrue();

        user.PasswordHash.ShouldBe("hash:a new long password");
        await sessionManager.Received(1).EndSessionAsync(open.Id, SessionEndReason.SecurityStampChanged, Arg.Any<CancellationToken>());
        log.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Security.PasswordResetCompleted);
        (await links.ResetPasswordAsync(token, "a new long password", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserTokenInvalid);
    }

    [Fact]
    public async Task ResetPassword_WithAnActivationToken_Fails()
    {
        var user = AddUser(password: null);
        await links.SendActivationAsync(user.Id, Ct);

        (await links.ResetPasswordAsync(TokenOf((string)dispatcher.Requests.Single().Model["link"]!), "a new long password", Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.UserTokenInvalid);
    }

    private static string TokenOf(string link) => HttpUtility.ParseQueryString(new Uri(link).Query)["token"]!;

    private User AddUser(string userName = "mario.rossi", string? password = "old password")
    {
        var user = User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), userName, "mario@example.test", "it", [TenantRole.Employee], isActive: true).Value;
        if (password is not null)
        {
            user.SetPassword("hash:" + password, PasswordFormat.Identity, time.GetUtcNow());
        }

        identity.Users.Add(user);
        return user;
    }
}

public sealed class ClientAndKeyManagerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddClient_ConfidentialGetsASecretStoredOnlyAsAHash()
    {
        var store = new InMemoryClientStore();
        var manager = new ClientApplicationManager(Platform.ManagerHarness.Runner(), store, new FakeHasher());

        var added = (await manager.AddAsync(new NewClientApplication("web", "Web app", ClientApplicationType.WebBff, ["https://app.example.test/"]), Ct)).Value;
        var mobile = (await manager.AddAsync(new NewClientApplication("mobile", "Mobile", ClientApplicationType.Mobile, []), Ct)).Value;

        added.Secret.ShouldNotBeNullOrEmpty();
        added.Client.SecretHash.ShouldBe("hash:" + added.Secret);
        added.Client.AllowedOrigins.ShouldBe(["https://app.example.test"]);
        mobile.Secret.ShouldBeNull();
        mobile.Client.SecretHash.ShouldBeNull();
        (await manager.ListAsync(Ct)).Select(client => client.ClientId).ShouldBe(["web", "mobile"]);
        (await manager.AddAsync(new NewClientApplication("web", "Again", ClientApplicationType.Mobile, []), Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.ClientIdTaken);
        (await manager.AddAsync(new NewClientApplication(" ", "Blank", ClientApplicationType.Mobile, []), Ct)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task RotateKeys_RetiresTheActiveKeyAndAddsANewOne()
    {
        var time = new ManualTimeProvider();
        var store = new InMemoryKeyStore();
        var factory = Substitute.For<ISigningKeyFactory>();
        factory.Create(Arg.Any<DateTimeOffset>()).Returns(call => new SigningKey(Guid.NewGuid().ToString("N"), "{}", "protected", call.Arg<DateTimeOffset>()));
        store.Add(factory.Create(time.GetUtcNow()));
        var log = new RecordingLogger<SigningKeyManager>();
        var manager = new SigningKeyManager(Platform.ManagerHarness.Runner(), store, factory, time, log);

        var kid = (await manager.RotateAsync(Ct)).Value;

        store.Keys.Single(key => key.IsActive).Id.ShouldBe(kid);
        var retired = store.Keys.Single(key => !key.IsActive);
        retired.PublishedUntil.ShouldBe(time.GetUtcNow() + SigningKeyManager.ValidationGrace);
        log.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Security.SigningKeyRotated);
    }

    private sealed class InMemoryKeyStore : ISigningKeyStore
    {
        public List<SigningKey> Keys { get; } = [];

        public Task<IReadOnlyList<SigningKey>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<SigningKey>>(Keys.ToArray());

        public void Add(SigningKey key) => Keys.Add(key);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
