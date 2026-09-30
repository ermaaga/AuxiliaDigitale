using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Identity;
using Auxilia.Application.Platform;
using Auxilia.Application.Tests.Execution;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;

using NSubstitute;

namespace Auxilia.Application.Tests.Identity;

public sealed class PlatformIdentityTests
{
    private const string Password = "a long enough password";

    private readonly InMemoryPlatformStore store = new();
    private readonly InMemoryClientStore clients = new();
    private readonly FakeHasher hasher = new();
    private readonly ManualTimeProvider time = new();
    private readonly FakeTotp totp;
    private readonly FakeAccessTokenIssuer issuer;
    private readonly InMemoryDenyList denyList = new();
    private readonly ICurrentUser currentUser = Substitute.For<ICurrentUser>();
    private readonly ITenantDirectory tenants = Substitute.For<ITenantDirectory>();
    private readonly RecordingLogger<PlatformAuthManager> authLog = new();
    private readonly RecordingLogger<ClientApplicationValidator> clientLog = new();
    private readonly PlatformAuthManager auth;
    private readonly PlatformUserManager users;

    public PlatformIdentityTests()
    {
        totp = new FakeTotp(time);
        issuer = new FakeAccessTokenIssuer(time);
        var console = ClientApplication.Create(Guid.CreateVersion7(), "console", "Console", ClientApplicationType.PlatformConsole).Value;
        console.SetSecretHash(hasher.Hash("console secret"));
        clients.Add(console);
        var web = ClientApplication.Create(Guid.CreateVersion7(), "web", "Web", ClientApplicationType.WebBff).Value;
        web.SetSecretHash(hasher.Hash("web secret"));
        clients.Add(web);
        tenants.FindBySlugAsync("acme", Arg.Any<CancellationToken>()).Returns(new TenantInfo(Guid.CreateVersion7(), "acme", TenantStatus.Suspended, "it", "Europe/Rome"));
        tenants.FindBySlugAsync("old", Arg.Any<CancellationToken>()).Returns(new TenantInfo(Guid.CreateVersion7(), "old", TenantStatus.Archived, "it", "Europe/Rome"));

        var runner = Platform.ManagerHarness.Runner();
        auth = new PlatformAuthManager(
            runner, store, hasher, totp, new PrefixProtector(), new ClientApplicationValidator(clients, hasher, clientLog), issuer, denyList,
            currentUser, tenants, time, authLog);
        users = new PlatformUserManager(runner, store, auth, time, new RecordingLogger<PlatformUserManager>());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ClientCredentials Console => new("console", "console secret");

    [Fact]
    public async Task Add_CreatesAUserWithAOneUseActivationToken_AndRejectsDuplicates()
    {
        var added = (await users.AddAsync(" ops@example.test ", "Ops", Ct)).Value;

        added.User.Email.ShouldBe("ops@example.test");
        added.User.IsEnrolled.ShouldBeFalse();
        added.ExpiresAt.ShouldBe(time.GetUtcNow() + PlatformIdentityPolicy.ActivationTokenLifetime);
        store.Tokens.ShouldHaveSingleItem().TokenHash.ShouldBe(SecureTokens.Hash(added.ActivationToken));
        (await users.AddAsync("OPS@example.test", "Again", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PlatformUserEmailTaken);
        (await users.AddAsync("not-an-email", "X", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserValueInvalid);
        (await users.AddAsync("x@example.test", " ", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserValueInvalid);
        (await users.ListAsync(Ct)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Activation_EnrolsTotpThenSetsThePasswordWithACurrentCode()
    {
        var token = (await users.AddAsync("ops@example.test", "Ops", Ct)).Value.ActivationToken;

        var enrollment = (await auth.BeginEnrollmentAsync(token, Ct)).Value;
        enrollment.Uri.ShouldBe($"otpauth://{enrollment.Secret}/ops@example.test/Auxilia");
        store.Users.Single().PendingTwoFactorSecret.ShouldBe("protected:" + enrollment.Secret);

        (await auth.ActivateAsync(token, Password, "wrong", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.TwoFactorCodeInvalid);
        (await auth.ActivateAsync(token, "short", FakeTotp.Valid, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PasswordTooWeak);
        (await auth.ActivateAsync(token, Password, FakeTotp.Valid, Ct)).IsSuccess.ShouldBeTrue();

        var user = store.Users.Single();
        (user.IsEnrolled, user.PasswordHash, user.TwoFactorSecret).ShouldBe((true, "hash:" + Password, "protected:" + enrollment.Secret));
        (await auth.ActivateAsync(token, Password, FakeTotp.Valid, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserTokenInvalid);
        (await auth.BeginEnrollmentAsync(token, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserTokenInvalid);
        (await auth.BeginEnrollmentAsync("", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserTokenInvalid);
    }

    [Fact]
    public async Task Activation_ExpiredTokenOrActivateWithoutEnrolment_Fails()
    {
        var token = (await users.AddAsync("ops@example.test", "Ops", Ct)).Value.ActivationToken;
        (await auth.ActivateAsync(token, Password, FakeTotp.Valid, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.TwoFactorCodeInvalid);

        time.Advance(PlatformIdentityPolicy.ActivationTokenLifetime);
        (await auth.BeginEnrollmentAsync(token, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserTokenInvalid);
    }

    [Fact]
    public async Task SignIn_NeedsPasswordAndAFreshCode_OnAConsoleClient()
    {
        await EnrolledUserAsync();

        var pair = (await auth.SignInAsync(SignIn(), Ct)).Value;

        var request = issuer.PlatformRequests.ShouldHaveSingleItem();
        (request.TenantSlug, request.SessionId, request.Lifetime).ShouldBe((null, pair.SessionId, PlatformIdentityPolicy.AccessTokenLifetime));
        store.Sessions.ShouldHaveSingleItem().AbsoluteExpiresAt.ShouldBe(time.GetUtcNow() + PlatformIdentityPolicy.SessionAbsolute);
        authLog.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Security.PlatformSignedIn);

        // The same code (same time step) cannot be used twice.
        (await auth.SignInAsync(SignIn(), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.InvalidCredentials);
        time.Advance(TimeSpan.FromSeconds(30));
        (await auth.SignInAsync(SignIn(), Ct)).IsSuccess.ShouldBeTrue();

        (await auth.SignInAsync(SignIn(client: new ClientCredentials("web", "web secret")), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.ClientInvalid);
        clientLog.Entries.ShouldContain(entry => (string?)entry.Properties["Reason"] == "audience");
    }

    [Fact]
    public async Task SignIn_FailuresAreGeneric_AndLockTheAccount()
    {
        await EnrolledUserAsync();
        await users.AddAsync("pending@example.test", "Pending", Ct);

        (await auth.SignInAsync(SignIn(email: "nobody@example.test"), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.InvalidCredentials);
        (await auth.SignInAsync(SignIn(email: "pending@example.test"), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.InvalidCredentials);
        for (var attempt = 0; attempt < PlatformIdentityPolicy.LockoutMaxFailedAttempts; attempt++)
        {
            (await auth.SignInAsync(SignIn(code: "wrong"), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.InvalidCredentials);
        }

        (await auth.SignInAsync(SignIn(), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.AccountLocked);
        authLog.Entries.Where(entry => entry.EventId.Id == EventCodes.Security.PlatformLoginFailed).Select(entry => entry.Properties["Reason"])
            .ShouldBe(["UnknownUser", "NotEnrolled", "WrongCode", "WrongCode", "WrongCode", "WrongCode", "WrongCode", "LockedOut"]);
        authLog.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Security.PlatformAccountLockedOut);

        time.Advance(PlatformIdentityPolicy.LockoutDuration);
        (await auth.SignInAsync(SignIn(password: "wrong password"), Ct)).IsFailure.ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(30));
        (await auth.SignInAsync(SignIn(), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Refresh_RotatesAndDetectsReuse_AndResetEndsTheSessions()
    {
        await EnrolledUserAsync();
        var first = (await auth.SignInAsync(SignIn(), Ct)).Value;

        var second = (await auth.RefreshAsync(new RefreshTokens(Console, first.RefreshToken, null, null), Ct)).Value;
        second.SessionId.ShouldBe(first.SessionId);
        (await auth.RefreshAsync(new RefreshTokens(Console, first.RefreshToken, null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.RefreshTokenInvalid);
        store.Sessions.Single().EndReason.ShouldBe(SessionEndReason.RefreshTokenReuse);
        denyList.Sessions.ShouldContainKey(first.SessionId);

        time.Advance(TimeSpan.FromSeconds(30));
        var third = (await auth.SignInAsync(SignIn(), Ct)).Value;
        var reset = (await users.ResetAsync("ops@example.test", Ct)).Value;
        reset.User.IsEnrolled.ShouldBeFalse();
        store.Sessions.Single(session => session.Id == third.SessionId).EndReason.ShouldBe(SessionEndReason.SecurityStampChanged);
        (await auth.RefreshAsync(new RefreshTokens(Console, third.RefreshToken, null, null), Ct)).IsFailure.ShouldBeTrue();
        (await users.ResetAsync("nobody@example.test", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
    }

    [Fact]
    public async Task Disable_EndsTheSessionsAndBlocksSignIn()
    {
        await EnrolledUserAsync();
        var pair = (await auth.SignInAsync(SignIn(), Ct)).Value;

        (await users.SetActiveAsync("ops@example.test", false, Ct)).IsSuccess.ShouldBeTrue();

        store.Sessions.Single().EndReason.ShouldBe(SessionEndReason.Revoked);
        (await auth.RefreshAsync(new RefreshTokens(Console, pair.RefreshToken, null, null), Ct)).IsFailure.ShouldBeTrue();
        time.Advance(TimeSpan.FromSeconds(30));
        (await auth.SignInAsync(SignIn(), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.InvalidCredentials);
        (await users.SetActiveAsync("ops@example.test", true, Ct)).IsSuccess.ShouldBeTrue();
        (await auth.SignInAsync(SignIn(), Ct)).IsFailure.ShouldBeTrue("same TOTP step as the failed attempt");
        time.Advance(TimeSpan.FromSeconds(30));
        (await auth.SignInAsync(SignIn(), Ct)).IsSuccess.ShouldBeTrue();
        (await users.SetActiveAsync("nobody@example.test", true, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
    }

    [Fact]
    public async Task EndSession_DeniesThePlatformSession()
    {
        await EnrolledUserAsync();
        var pair = (await auth.SignInAsync(SignIn(), Ct)).Value;

        (await auth.EndSessionAsync(pair.SessionId, SessionEndReason.Logout, Ct)).IsSuccess.ShouldBeTrue();

        denyList.Sessions[pair.SessionId].ShouldBe(time.GetUtcNow() + PlatformIdentityPolicy.AccessTokenLifetime);
        (await auth.EndSessionAsync(Guid.CreateVersion7(), SessionEndReason.Logout, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.RefreshTokenInvalid);
    }

    [Fact]
    public async Task OpenTenant_OnlyForPlatformUsers_AndNotForArchivedTenants()
    {
        var sessionId = Guid.CreateVersion7();
        currentUser.ActorType.Returns(ActorType.User);
        currentUser.UserId.Returns(Guid.CreateVersion7());
        (await auth.OpenTenantAsync("acme", sessionId, "console", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PlatformAccessRequired);

        currentUser.ActorType.Returns(ActorType.Platform);
        var issued = (await auth.OpenTenantAsync(" ACME ", sessionId, "console", Ct)).Value;

        issued.Token.ShouldBe($"pt:{sessionId}:acme");
        issuer.PlatformRequests.ShouldHaveSingleItem().Lifetime.ShouldBe(PlatformIdentityPolicy.TenantTokenLifetime);
        authLog.Entries.ShouldContain(entry => entry.EventId.Id == EventCodes.Security.PlatformTenantAccess);
        (await auth.OpenTenantAsync("old", sessionId, "console", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantNotFound);
        (await auth.OpenTenantAsync("missing", sessionId, "console", Ct)).Error!.Code.ShouldBe(EventCodes.Tenancy.TenantNotFound);
    }

    [Fact]
    public async Task ConsoleQueries_ReturnTheUserAndTheNonArchivedTenants()
    {
        var user = (await users.AddAsync("ops@example.test", "Ops", Ct)).Value.User;
        var catalog = Substitute.For<ICatalogStore>();
        var active = Tenant.Create(Guid.CreateVersion7(), "beta", "Beta", "it", "Europe/Rome").Value;
        active.Activate();
        var archived = Tenant.Create(Guid.CreateVersion7(), "alpha", "Alpha", "it", "Europe/Rome").Value;
        archived.Archive(time.GetUtcNow());
        catalog.ListTenantsAsync(Arg.Any<CancellationToken>()).Returns([active, archived]);
        var console = new PlatformConsoleQueryService(currentUser, store, catalog);

        currentUser.ActorType.Returns(ActorType.User);
        (await console.GetMeAsync(Ct)).Error!.Code.ShouldBe(EventCodes.Identity.UserNotFound);
        currentUser.ActorType.Returns(ActorType.Platform);
        currentUser.UserId.Returns(user.Id);
        var me = (await console.GetMeAsync(Ct)).Value;

        (me.Email, me.DisplayName).ShouldBe(("ops@example.test", "Ops"));
        me.Roles.ShouldBe(["System"]);
        (await console.ListTenantsAsync(Ct)).ShouldHaveSingleItem().ShouldBe(new Contracts.Platform.PlatformTenantResponse("beta", "Beta", "Active", null));
    }

    private static PlatformSignIn SignIn(ClientCredentials? client = null, string email = "ops@example.test", string password = Password, string code = FakeTotp.Valid) =>
        new(client ?? Console, email, password, code, "10.0.0.1", "tests");

    private async Task EnrolledUserAsync()
    {
        var token = (await users.AddAsync("ops@example.test", "Ops", Ct)).Value.ActivationToken;
        await auth.BeginEnrollmentAsync(token, Ct);
        (await auth.ActivateAsync(token, Password, FakeTotp.Valid, Ct)).IsSuccess.ShouldBeTrue();

        // The activation used the current step: sign-ins start at the next one.
        time.Advance(TimeSpan.FromSeconds(30));
    }

    private sealed class FakeTotp(TimeProvider time) : ITotpService
    {
        public const string Valid = "000000";

        private int secrets;

        public string NewSecret() => $"SECRET{++secrets}";

        public string EnrollmentUri(string secret, string accountName, string issuer) => $"otpauth://{secret}/{accountName}/{issuer}";

        public long? Verify(string secret, string code, DateTimeOffset now) =>
            code == Valid && secret.StartsWith("SECRET", StringComparison.Ordinal) ? time.GetUtcNow().ToUnixTimeSeconds() / 30 : null;
    }

    private sealed class PrefixProtector : ITwoFactorSecretProtector
    {
        public string Protect(string secret) => "protected:" + secret;

        public string Unprotect(string protectedSecret) => protectedSecret["protected:".Length..];
    }

    private sealed class InMemoryPlatformStore : IPlatformIdentityStore
    {
        public List<PlatformUser> Users { get; } = [];

        public List<UserToken> Tokens { get; } = [];

        public List<RefreshSession> Sessions { get; } = [];

        public List<RefreshToken> RefreshTokens { get; } = [];

        public Task<PlatformUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Users.SingleOrDefault(user => user.Id == userId));

        public Task<PlatformUser?> FindUserByEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<PlatformUser>> ListUsersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlatformUser>>(Users.ToArray());

        public Task<UserToken?> FindUserTokenAsync(string tokenHash, UserTokenPurpose purpose, CancellationToken cancellationToken) =>
            Task.FromResult(Tokens.SingleOrDefault(token => token.TokenHash == tokenHash && token.Purpose == purpose));

        public Task<IReadOnlyList<UserToken>> UnusedUserTokensAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserToken>>(Tokens.Where(token => token.UserId == userId && token.Purpose == purpose && token.UsedAt is null).ToArray());

        public Task<RefreshSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken) => Task.FromResult(Sessions.SingleOrDefault(session => session.Id == sessionId));

        public Task<IReadOnlyList<RefreshSession>> OpenSessionsOfUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RefreshSession>>(Sessions.Where(session => session.UserId == userId && session.EndedAt is null).ToArray());

        public Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(RefreshTokens.SingleOrDefault(token => token.TokenHash == tokenHash));

        public void Add(PlatformUser user) => Users.Add(user);

        public void Add(UserToken token) => Tokens.Add(token);

        public void Add(RefreshSession session) => Sessions.Add(session);

        public void Add(RefreshToken token) => RefreshTokens.Add(token);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
