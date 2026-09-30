using Auxilia.Application.Identity;
using Auxilia.Application.Tests.Execution;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Tests.Identity;

public sealed class SessionManagerTests : IAsyncDisposable
{
    private const string Password = "the right password";

    private readonly InMemoryIdentityData identity = new();
    private readonly InMemorySessionData sessions;
    private readonly InMemoryClientStore clients = new();
    private readonly FakeHasher hasher = new();
    private readonly ManualTimeProvider time = new();
    private readonly FakeAccessTokenIssuer issuer;
    private readonly InMemoryDenyList denyList = new();
    private readonly RecordingRealtimeNotifier realtime = new();
    private readonly RecordingLogger<SessionManager> log = new();
    private readonly RecordingLogger<ClientApplicationValidator> clientLog = new();

    public SessionManagerTests()
    {
        sessions = new InMemorySessionData(identity);
        issuer = new FakeAccessTokenIssuer(time);
        clients.Add(Client("web", ClientApplicationType.WebBff, secret: "web secret"));
        clients.Add(Client("mobile", ClientApplicationType.Mobile, secret: null));
        var disabled = Client("old", ClientApplicationType.Mobile, secret: null);
        disabled.Disable();
        clients.Add(disabled);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await sessions.DisposeAsync();
        await identity.DisposeAsync();
    }

    private static ClientCredentials Web => new("web", "web secret");

    [Fact]
    public async Task SignIn_OpensASessionAndIssuesATokenPair()
    {
        var user = AddUser();
        var manager = Manager();

        var pair = (await manager.SignInAsync(new PasswordSignIn(Web, "mario.rossi", Password, "10.0.0.1", "tests"), Ct)).Value;

        var session = sessions.Sessions.ShouldHaveSingleItem();
        (session.Id, session.UserId, session.ClientId, session.IpAddress).ShouldBe((pair.SessionId, user.Id, "web", "10.0.0.1"));
        session.IdleExpiresAt.ShouldBe(time.GetUtcNow() + TimeSpan.FromMinutes(IdentitySettings.SessionIdleMinutes.Default));
        session.AbsoluteExpiresAt.ShouldBe(time.GetUtcNow() + TimeSpan.FromDays(IdentitySettings.SessionAbsoluteDays.Default));
        sessions.RefreshTokens.ShouldHaveSingleItem().TokenHash.ShouldBe(SecureTokens.Hash(pair.RefreshToken));
        pair.RefreshToken.ShouldNotContain('=');

        var request = issuer.Requests.ShouldHaveSingleItem();
        (request.UserId, request.TenantSlug, request.ClientId).ShouldBe((user.Id, "acme", "web"));
        request.Roles.ShouldBe([TenantRole.Employee]);
        request.Lifetime.ShouldBe(TimeSpan.FromMinutes(IdentitySettings.AccessTokenMinutes.Default));
    }

    [Theory]
    [InlineData("unknown", null)]
    [InlineData("web", null)]
    [InlineData("web", "wrong")]
    [InlineData("old", null)]
    public async Task SignIn_WithAnInvalidClient_FailsBeforeCheckingThePassword(string clientId, string? secret)
    {
        AddUser();

        var result = await Manager().SignInAsync(new PasswordSignIn(new ClientCredentials(clientId, secret), "mario.rossi", Password, null, null), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Identity.ClientInvalid);
        sessions.Sessions.ShouldBeEmpty();
        clientLog.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Security.ClientRejected);
    }

    [Fact]
    public async Task SignIn_PublicClientNeedsNoSecret_AndWrongPasswordsFail()
    {
        AddUser();
        var manager = Manager();

        (await manager.SignInAsync(new PasswordSignIn(new ClientCredentials("mobile", null), "mario.rossi", Password, null, null), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.SignInAsync(new PasswordSignIn(Web, "mario.rossi", "wrong", null, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.InvalidCredentials);
    }

    [Fact]
    public async Task SignIn_WithSingleSession_EndsTheOtherSessionsOfTheUser()
    {
        AddUser();
        var manager = Manager(singleSession: true);

        var first = (await manager.SignInAsync(SignIn(), Ct)).Value;
        var second = (await manager.SignInAsync(SignIn(), Ct)).Value;

        var ended = sessions.Sessions.Single(session => session.Id == first.SessionId);
        ended.EndReason.ShouldBe(SessionEndReason.SingleSession);
        denyList.Sessions.ShouldContainKey(first.SessionId);
        sessions.Sessions.Single(session => session.Id == second.SessionId).EndedAt.ShouldBeNull();
        var push = realtime.Pushes.ShouldHaveSingleItem();
        (push.Target, push.EventName, push.Payload).ShouldBe(($"session:{first.SessionId}", RealtimeEvents.ForceLogout, new ForceLogoutEvent("SingleSession")));
    }

    [Fact]
    public async Task Refresh_RotatesTheRefreshToken()
    {
        AddUser();
        var manager = Manager();
        var first = (await manager.SignInAsync(SignIn(), Ct)).Value;
        time.Advance(TimeSpan.FromMinutes(30));

        var second = (await manager.RefreshAsync(new RefreshTokens(Web, first.RefreshToken, null, null), Ct)).Value;

        second.SessionId.ShouldBe(first.SessionId);
        second.RefreshToken.ShouldNotBe(first.RefreshToken);
        sessions.RefreshTokens.Single(token => token.TokenHash == SecureTokens.Hash(first.RefreshToken)).IsConsumed.ShouldBeTrue();
        var session = sessions.Sessions.ShouldHaveSingleItem();
        session.LastUsedAt.ShouldBe(time.GetUtcNow());
        session.IdleExpiresAt.ShouldBe(time.GetUtcNow() + TimeSpan.FromMinutes(IdentitySettings.SessionIdleMinutes.Default));
    }

    [Fact]
    public async Task Refresh_ReusingARotatedToken_RevokesTheWholeSession()
    {
        AddUser();
        var manager = Manager();
        var first = (await manager.SignInAsync(SignIn(), Ct)).Value;
        var second = (await manager.RefreshAsync(new RefreshTokens(Web, first.RefreshToken, null, null), Ct)).Value;

        (await manager.RefreshAsync(new RefreshTokens(Web, first.RefreshToken, null, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.RefreshTokenInvalid);

        sessions.Sessions.ShouldHaveSingleItem().EndReason.ShouldBe(SessionEndReason.RefreshTokenReuse);
        denyList.Sessions.ShouldContainKey(first.SessionId);
        log.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Security.RefreshTokenReuse);
        realtime.Pushes.ShouldHaveSingleItem().Payload.ShouldBe(new ForceLogoutEvent("RefreshTokenReuse"));
        (await manager.RefreshAsync(new RefreshTokens(Web, second.RefreshToken, null, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.RefreshTokenInvalid);
    }

    [Fact]
    public async Task Refresh_FailsForUnknownTokensOtherClientsAndExpiredSessions()
    {
        AddUser();
        var manager = Manager();
        var pair = (await manager.SignInAsync(SignIn(), Ct)).Value;

        (await manager.RefreshAsync(new RefreshTokens(Web, "not a token", null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.RefreshTokenInvalid);
        (await manager.RefreshAsync(new RefreshTokens(Web, "", null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.RefreshTokenInvalid);
        (await manager.RefreshAsync(new RefreshTokens(new ClientCredentials("mobile", null), pair.RefreshToken, null, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.RefreshTokenInvalid);
        (await manager.RefreshAsync(new RefreshTokens(new ClientCredentials("web", "wrong"), pair.RefreshToken, null, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Identity.ClientInvalid);

        time.Advance(TimeSpan.FromMinutes(IdentitySettings.SessionIdleMinutes.Default + 1));
        (await manager.RefreshAsync(new RefreshTokens(Web, pair.RefreshToken, null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.RefreshTokenInvalid);
    }

    [Fact]
    public async Task Refresh_AfterAPasswordChangeOrDeactivation_EndsTheSession()
    {
        var user = AddUser();
        var manager = Manager();
        var pair = (await manager.SignInAsync(SignIn(), Ct)).Value;

        user.SetPassword("hash:another password", PasswordFormat.Identity, time.GetUtcNow());

        (await manager.RefreshAsync(new RefreshTokens(Web, pair.RefreshToken, null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.RefreshTokenInvalid);
        sessions.Sessions.ShouldHaveSingleItem().EndReason.ShouldBe(SessionEndReason.SecurityStampChanged);
    }

    [Fact]
    public async Task EndSession_DeniesTheSessionUntilItsAccessTokensExpire_AndIsIdempotent()
    {
        AddUser();
        var manager = Manager();
        var pair = (await manager.SignInAsync(SignIn(), Ct)).Value;

        (await manager.EndSessionAsync(pair.SessionId, SessionEndReason.Logout, Ct)).IsSuccess.ShouldBeTrue();
        (await manager.EndSessionAsync(pair.SessionId, SessionEndReason.Revoked, Ct)).IsSuccess.ShouldBeTrue();

        var session = sessions.Sessions.ShouldHaveSingleItem();
        session.EndReason.ShouldBe(SessionEndReason.Logout);
        realtime.Pushes.ShouldHaveSingleItem().ShouldBe(($"session:{pair.SessionId}", RealtimeEvents.ForceLogout, (object)new ForceLogoutEvent("Logout")));
        denyList.Sessions[pair.SessionId].ShouldBe(time.GetUtcNow() + TimeSpan.FromMinutes(IdentitySettings.AccessTokenMinutes.Default));
        log.Entries.Where(entry => entry.EventId.Id == EventCodes.Security.SessionEnded).ShouldHaveSingleItem()
            .Properties["Reason"].ShouldBe("Logout");
        (await manager.EndSessionAsync(Guid.CreateVersion7(), SessionEndReason.Logout, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.RefreshTokenInvalid);
        (await manager.RefreshAsync(new RefreshTokens(Web, pair.RefreshToken, null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.RefreshTokenInvalid);
    }

    private static PasswordSignIn SignIn() => new(Web, "mario.rossi", Password, null, null);

    private ClientApplication Client(string clientId, ClientApplicationType type, string? secret)
    {
        var client = ClientApplication.Create(Guid.CreateVersion7(), clientId, clientId, type).Value;
        if (secret is not null)
        {
            client.SetSecretHash(hasher.Hash(secret));
        }

        return client;
    }

    private User AddUser()
    {
        var user = User.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "mario.rossi", "mario@example.test", "it", [TenantRole.Employee], isActive: true).Value;
        user.SetPassword("hash:" + Password, PasswordFormat.Identity, time.GetUtcNow());
        identity.Users.Add(user);
        return user;
    }

    private SessionManager Manager(bool singleSession = false)
    {
        var settings = SessionSettings.Create(singleSession);
        var runner = Platform.ManagerHarness.Runner();
        var authenticator = new PasswordAuthenticator(runner, identity, hasher, settings, time, new RecordingLogger<PasswordAuthenticator>());
        return new SessionManager(
            runner, sessions, authenticator, new ClientApplicationValidator(clients, hasher, clientLog), issuer, denyList, settings,
            SessionSettings.Tenant(), realtime, time, log);
    }
}
