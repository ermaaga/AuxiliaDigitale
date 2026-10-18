using System.Net;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Mailbox;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Mailbox;
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Mailbox;

public sealed class ConnectedAccountTests : IAsyncDisposable
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid SomeoneElse = Guid.CreateVersion7();

    private readonly InMemoryConnectedAccounts data = new();
    private readonly FakeOAuthProvider google = new(ConnectedAccountProvider.Google);
    private readonly FakeOAuthProvider microsoft = new(ConnectedAccountProvider.Microsoft) { IsConfigured = false };
    private readonly IImapConnectionTester imap = Substitute.For<IImapConnectionTester>();
    private readonly IHostAddressResolver resolver = Substitute.For<IHostAddressResolver>();
    private readonly ISettingsProvider settings = Substitute.For<ISettingsProvider>();
    private readonly IAccessGuard guard = Substitute.For<IAccessGuard>();
    private readonly IPermissionAccess permissions = Substitute.For<IPermissionAccess>();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ManualTimeProvider clock = new();
    private readonly ConnectedAccountManager manager;
    private readonly ConnectedAccountQueryService query;

    public ConnectedAccountTests()
    {
        CallAs(Me);
        guard.EnsureAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        permissions.HasAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        settings.GetAsync(MailboxSettings.ImapAllowedPorts, Arg.Any<CancellationToken>()).Returns("993,143");
        resolver.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([IPAddress.Parse("62.149.157.1")]);
        imap.TestAsync(Arg.Any<ImapConnectionTarget>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ImapTestOutcome.Success);
        var policy = new ConnectedAccountPolicy(caller);
        manager = new ConnectedAccountManager(
            ManagerHarness.Runner(), data, [google, microsoft], new ReversingProtector(), imap, resolver, settings, guard, policy, clock,
            NullLogger<ConnectedAccountManager>.Instance);
        query = new ConnectedAccountQueryService(data, [google, microsoft], permissions, policy);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => data.DisposeAsync();

    private static ConnectImapRequest Imap(string host = "imaps.pec.aruba.it", int port = 993, string security = "SslOnConnect", string password = "secret") =>
        new("Studio@pec.it", "studio@pec.it", password, host, port, security);

    private void CallAs(Guid userId, ActorType actor = ActorType.User)
    {
        caller.ActorType.Returns(actor);
        caller.UserId.Returns(userId);
        caller.Roles.Returns([TenantRole.Employee]);
    }

    private async Task<(string State, OAuthAuthorization Authorization)> StartAsync(bool mail = true, bool calendar = false, Guid? accountId = null)
    {
        var started = await manager.StartOAuthAsync("google", new StartOAuthRequest(mail, calendar, accountId), Ct);
        started.IsSuccess.ShouldBeTrue();
        var authorization = google.LastAuthorization!;
        return (authorization.State, authorization);
    }

    [Fact]
    public async Task StartOAuth_StoresTheStateHashAndProtectedVerifier_AndBuildsTheConsentUrl()
    {
        var (state, authorization) = await StartAsync(mail: true, calendar: true);

        var stored = data.Requests.Single();
        stored.StateHash.ShouldBe(OAuthSecrets.Hash(state));
        stored.StateHash.ShouldNotContain(state);
        (stored.UserId, stored.Provider, stored.WantsMail, stored.WantsCalendar).ShouldBe((Me, ConnectedAccountProvider.Google, true, true));
        var verifier = ReversingProtector.Reverse(stored.ProtectedVerifier);
        authorization.CodeChallenge.ShouldBe(OAuthSecrets.Challenge(verifier));
        authorization.CodeChallenge.ShouldNotBe(verifier);
        (authorization.Mail, authorization.Calendar, authorization.LoginHint).ShouldBe((true, true, (string?)null));
    }

    [Fact]
    public async Task StartOAuth_RefusesUnknownOrUnconfiguredProviders_AndNoCapability()
    {
        (await manager.StartOAuthAsync("microsoft", new StartOAuthRequest(true, false, null), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.ProviderNotAvailable);
        (await manager.StartOAuthAsync("imap", new StartOAuthRequest(true, false, null), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.ProviderNotAvailable);
        (await manager.StartOAuthAsync("0", new StartOAuthRequest(true, false, null), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.ProviderNotAvailable);
        (await manager.StartOAuthAsync("google", new StartOAuthRequest(false, false, null), Ct)).Error!.ValidationErrors.Keys.ShouldBe(["capabilities"]);
        data.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task StartOAuth_DeletesExpiredRequests()
    {
        await StartAsync();
        clock.Advance(OAuthRequest.Lifetime + TimeSpan.FromMinutes(1));

        await StartAsync();

        data.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task CompleteOAuth_ConnectsTheAccountWithTheGrantedCapabilities()
    {
        var (state, _) = await StartAsync(mail: true, calendar: true);
        google.NextTokens = Tokens(mail: true, calendar: false);

        var connected = (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(state, "code-1", null), Ct)).Value;

        (connected.Provider, connected.Address, connected.HasMail, connected.HasCalendar, connected.Color, connected.Status)
            .ShouldBe(("google", "mario@gmail.com", true, false, "blue", "Active"));
        var account = data.Accounts.Single();
        (account.UserId, account.ProtectedSecret).ShouldBe((Me, ReversingProtector.Reverse("refresh-1")));
        google.LastCodeVerifier.ShouldBe(ReversingProtector.Reverse(data.Requests.Single().ProtectedVerifier));
        data.Requests.Single().UsedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task CompleteOAuth_TheSameAccountAgain_IsReconnectedWithMoreCapabilities()
    {
        var (first, _) = await StartAsync();
        google.NextTokens = Tokens(mail: true, calendar: false);
        var account = (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(first, "code-1", null), Ct)).Value;

        var (second, authorization) = await StartAsync(mail: false, calendar: true, accountId: account.Id);
        (authorization.Mail, authorization.Calendar, authorization.LoginHint).ShouldBe((true, true, "mario@gmail.com"));
        google.NextTokens = Tokens(mail: true, calendar: true) with { RefreshToken = null };
        var again = (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(second, "code-2", null), Ct)).Value;

        again.Id.ShouldBe(account.Id);
        (again.HasMail, again.HasCalendar).ShouldBe((true, true));
        data.Accounts.Single().ProtectedSecret.ShouldBe(ReversingProtector.Reverse("refresh-1"));
    }

    [Fact]
    public async Task CompleteOAuth_RefusesAnUnknownReplayedOrForeignState()
    {
        (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest("forged", "code", null), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.OAuthStateInvalid);

        var (state, _) = await StartAsync();
        CallAs(SomeoneElse);
        (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(state, "code", null), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.OAuthStateInvalid);

        CallAs(Me);
        google.NextTokens = Tokens();
        (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(state, "code", null), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(state, "code", null), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.OAuthStateInvalid);
        google.Exchanges.ShouldBe(1);
    }

    [Fact]
    public async Task CompleteOAuth_AnExpiredState_IsRefusedWithoutCallingTheProvider()
    {
        var (state, _) = await StartAsync();
        clock.Advance(OAuthRequest.Lifetime);

        (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(state, "code", null), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.OAuthStateInvalid);
        google.Exchanges.ShouldBe(0);
    }

    [Fact]
    public async Task CompleteOAuth_ConsentRefused_OrNothingGranted_OrNoRefreshToken_ConnectsNothing()
    {
        var (denied, _) = await StartAsync();
        (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(denied, null, "access_denied"), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.OAuthDenied);
        data.Requests.Single(request => request.StateHash == OAuthSecrets.Hash(denied)).UsedAt.ShouldNotBeNull();

        var (unticked, _) = await StartAsync();
        google.NextTokens = Tokens(mail: false, calendar: false);
        (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(unticked, "code", null), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.OAuthDenied);

        var (noRefresh, _) = await StartAsync();
        google.NextTokens = Tokens() with { RefreshToken = null };
        (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(noRefresh, "code", null), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.OAuthExchangeFailed);

        var (failed, _) = await StartAsync();
        google.NextTokens = null;
        (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(failed, "code", null), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.OAuthExchangeFailed);

        data.Accounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConnectImap_TestsTheConnectionOnTheCheckedAddress_AndStoresTheProtectedPassword()
    {
        var connected = (await manager.ConnectImapAsync(Imap(), Ct)).Value;

        (connected.Provider, connected.Address, connected.HasMail, connected.HasCalendar, connected.ImapHost, connected.ImapPort)
            .ShouldBe(("imap", "studio@pec.it", true, false, "imaps.pec.aruba.it", (int?)993));
        data.Accounts.Single().ProtectedSecret.ShouldBe(ReversingProtector.Reverse("secret"));
        await imap.Received(1).TestAsync(
            Arg.Is<ImapConnectionTarget>(target => target.Host == "imaps.pec.aruba.it" && target.Address.Equals(IPAddress.Parse("62.149.157.1")) && target.Security == ImapSecurity.SslOnConnect),
            "studio@pec.it", "secret", Arg.Any<CancellationToken>());

        (await manager.ConnectImapAsync(Imap(password: "changed", port: 143, security: "StartTls"), Ct)).Value.Id.ShouldBe(connected.Id);
        (data.Accounts.Single().ProtectedSecret, data.Accounts.Single().ImapPort).ShouldBe((ReversingProtector.Reverse("changed"), (int?)143));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("169.254.169.254")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    public async Task ConnectImap_RefusesAServerResolvingToANonPublicAddress(string address)
    {
        resolver.ResolveAsync("evil.example.com", Arg.Any<CancellationToken>()).Returns([IPAddress.Parse("93.184.216.34"), IPAddress.Parse(address)]);

        (await manager.ConnectImapAsync(Imap(host: "evil.example.com"), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.ImapHostRefused);
        await imap.DidNotReceive().TestAsync(Arg.Any<ImapConnectionTarget>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConnectImap_ChecksTheInput_ThePortSetting_AndTheConnectionOutcome()
    {
        var invalid = await manager.ConnectImapAsync(new ConnectImapRequest("nope", " ", "", "127.0.0.1", 993, "Plain"), Ct);
        invalid.Error!.ValidationErrors.Keys.ShouldBe(["address", "userName", "password", "host", "port"], ignoreOrder: true);

        (await manager.ConnectImapAsync(Imap(port: 25), Ct)).Error!.ValidationErrors["port"].ShouldBe(["validation.mailbox.portNotAllowed"]);

        resolver.ResolveAsync("nowhere.example.com", Arg.Any<CancellationToken>()).Returns([]);
        (await manager.ConnectImapAsync(Imap(host: "nowhere.example.com"), Ct)).Error!.ValidationErrors["host"].ShouldBe(["validation.mailbox.hostUnknown"]);

        imap.TestAsync(Arg.Any<ImapConnectionTarget>(), Arg.Any<string>(), "wrong", Arg.Any<CancellationToken>()).Returns(ImapTestOutcome.AuthenticationFailed);
        (await manager.ConnectImapAsync(Imap(password: "wrong"), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.ImapAuthenticationFailed);

        imap.TestAsync(Arg.Any<ImapConnectionTarget>(), Arg.Any<string>(), "timeout", Arg.Any<CancellationToken>()).Returns(ImapTestOutcome.ConnectionFailed);
        (await manager.ConnectImapAsync(Imap(password: "timeout"), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.ImapConnectionFailed);

        data.Accounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Accounts_AreVisibleAndChangeableOnlyByTheirOwner()
    {
        var mine = (await manager.ConnectImapAsync(Imap(), Ct)).Value;

        CallAs(SomeoneElse);
        (await query.ListAsync(Ct)).Value.ShouldBeEmpty();
        (await manager.UpdateAsync(mine.Id, new UpdateConnectedAccountRequest("red"), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.AccountNotFound);
        (await manager.DisconnectAsync(mine.Id, Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.AccountNotFound);
        (await manager.StartOAuthAsync("google", new StartOAuthRequest(true, false, mine.Id), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.AccountNotFound);

        CallAs(Me);
        (await query.ListAsync(Ct)).Value.Select(account => account.Id).ShouldBe([mine.Id]);
        (await manager.UpdateAsync(mine.Id, new UpdateConnectedAccountRequest("red"), Ct)).Value.Color.ShouldBe("red");
        (await manager.UpdateAsync(mine.Id, new UpdateConnectedAccountRequest("magenta"), Ct)).Error!.Code.ShouldBe(EventCodes.Mailbox.AccountInvalid);
    }

    [Fact]
    public async Task PlatformCallers_HaveNoAccounts()
    {
        CallAs(Guid.CreateVersion7(), ActorType.Platform);

        (await query.ListAsync(Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
        (await manager.ConnectImapAsync(Imap(), Ct)).IsFailure.ShouldBeTrue();
        (await manager.StartOAuthAsync("google", new StartOAuthRequest(true, false, null), Ct)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Disconnect_DeletesTheAccount_AndRevokesTheGoogleGrant()
    {
        var (state, _) = await StartAsync();
        google.NextTokens = Tokens();
        var account = (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(state, "code", null), Ct)).Value;

        (await manager.DisconnectAsync(account.Id, Ct)).IsSuccess.ShouldBeTrue();

        data.Accounts.ShouldBeEmpty();
        google.Revoked.ShouldBe(["refresh-1"]);
    }

    [Fact]
    public async Task Disconnect_SucceedsWhenTheProviderCannotRevoke()
    {
        var (state, _) = await StartAsync();
        google.NextTokens = Tokens();
        var account = (await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(state, "code", null), Ct)).Value;
        google.RevokeThrows = true;

        (await manager.DisconnectAsync(account.Id, Ct)).IsSuccess.ShouldBeTrue();
        data.Accounts.ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveAll_DeletesTheAccountsOfTheLeavingUserOnly_AndRevokesTheirGrants()
    {
        var (state, _) = await StartAsync();
        google.NextTokens = Tokens();
        await manager.CompleteOAuthAsync("google", new CompleteOAuthRequest(state, "code", null), Ct);
        (await manager.ConnectImapAsync(Imap(), Ct)).IsSuccess.ShouldBeTrue();
        CallAs(SomeoneElse);
        (await manager.ConnectImapAsync(Imap(), Ct)).IsSuccess.ShouldBeTrue();
        var cleanup = new ConnectedAccountCleanup(ManagerHarness.Runner(), data, [google, microsoft], new ReversingProtector(), NullLogger<ConnectedAccountCleanup>.Instance);

        (await cleanup.RemoveAllAsync(Me, Ct)).IsSuccess.ShouldBeTrue();
        (await cleanup.RemoveAllAsync(Guid.CreateVersion7(), Ct)).IsSuccess.ShouldBeTrue();

        data.Accounts.ShouldHaveSingleItem().UserId.ShouldBe(SomeoneElse);
        google.Revoked.ShouldBe(["refresh-1"]);
    }

    [Fact]
    public async Task Providers_OffersOnlyConfiguredOAuthProviders_AndImap()
    {
        (await query.ProvidersAsync(Ct)).Value.Select(provider => (provider.Provider, provider.SupportsCalendar)).ShouldBe([("google", true), ("imap", false)]);

        microsoft.IsConfigured = true;
        (await query.ProvidersAsync(Ct)).Value.Select(provider => provider.Provider).ShouldBe(["google", "microsoft", "imap"]);
    }

    [Fact]
    public async Task SuggestImapServer_KnowsThePecProviders()
    {
        (await query.SuggestImapServerAsync("Studio@Legalmail.it", Ct)).Value.ShouldBe(new ImapServerSuggestionResponse("mbox.cert.legalmail.it", 993, "SslOnConnect"));
        (await query.SuggestImapServerAsync("info@example.org", Ct)).Value.ShouldBe(new ImapServerSuggestionResponse(null, null, null));
        (await query.SuggestImapServerAsync(null, Ct)).Value.Host.ShouldBeNull();
    }

    private static OAuthTokens Tokens(bool mail = true, bool calendar = false) =>
        new("access", "refresh-1", DateTimeOffset.UnixEpoch, "openid email", mail, calendar, "Mario@Gmail.com", "Mario Rossi");
}

public sealed class OutboundAddressPolicyTests
{
    [Theory]
    [InlineData("62.149.157.1", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2a00:1450:4002:41b::6d", true)]
    [InlineData("0.0.0.0", false)]
    [InlineData("10.20.30.40", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("127.0.0.53", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("255.255.255.255", false)]
    [InlineData("::", false)]
    [InlineData("::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:8.8.8.8", true)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("64:ff9b::a9fe:a9fe", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("2002:a9fe:a9fe::1", false)]
    public void IsAllowed_AcceptsPublicUnicastOnly(string address, bool allowed) =>
        OutboundAddressPolicy.IsAllowed(IPAddress.Parse(address)).ShouldBe(allowed);
}

public sealed class OAuthSecretsTests
{
    [Fact]
    public void Challenge_IsTheRfc7636Example()
    {
        OAuthSecrets.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk").ShouldBe("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    }

    [Fact]
    public void NewRandom_Is256BitBase64Url_AndHashIsHex()
    {
        var value = OAuthSecrets.NewRandom();

        value.Length.ShouldBe(43);
        value.ShouldAllBe(character => char.IsAsciiLetterOrDigit(character) || character == '-' || character == '_');
        OAuthSecrets.NewRandom().ShouldNotBe(value);
        OAuthSecrets.Hash(value).Length.ShouldBe(OAuthRequest.StateHashLength);
    }

    [Fact]
    public void ParsePorts_RefusesWhatIsNotAPort()
    {
        MailboxSettings.ParsePorts("993, 143").ShouldBe(new HashSet<int> { 993, 143 });
        MailboxSettings.ParsePorts("993,abc").ShouldBeNull();
        MailboxSettings.ParsePorts("70000").ShouldBeNull();
        MailboxSettings.ImapAllowedPorts.TryRead("\"\"", out _).ShouldBeFalse();
        MailboxSettings.ImapAllowedPorts.TryRead("\"993\"", out _).ShouldBeTrue();
    }
}

/// <summary>"Protects" by reversing the text: enough to prove the stored value is never the clear one.</summary>
internal sealed class ReversingProtector : IConnectedAccountSecretProtector
{
    public static string Reverse(string value) => new([.. value.Reverse()]);

    public string Protect(string secret) => Reverse(secret);

    public string Unprotect(string protectedSecret) => Reverse(protectedSecret);
}

internal sealed class FakeOAuthProvider(ConnectedAccountProvider provider) : IOAuthProvider
{
    public ConnectedAccountProvider Provider { get; } = provider;

    public bool IsConfigured { get; set; } = true;

    public bool SupportsCalendar => true;

    public OAuthAuthorization? LastAuthorization { get; private set; }

    public string? LastCodeVerifier { get; private set; }

    public OAuthTokens? NextTokens { get; set; }

    public int Exchanges { get; private set; }

    public bool RevokeThrows { get; set; }

    public List<string> Revoked { get; } = [];

    public Uri AuthorizationUrl(OAuthAuthorization authorization)
    {
        LastAuthorization = authorization;
        return new Uri($"https://consent.example/?state={authorization.State}");
    }

    public Task<Result<OAuthTokens>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken)
    {
        Exchanges++;
        LastCodeVerifier = codeVerifier;
        return Task.FromResult(NextTokens is { } tokens ? Result.Success(tokens) : Result.Failure<OAuthTokens>(Errors.Mailbox.OAuthExchangeFailed()));
    }

    public Task<Result<OAuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<OAuthRevocation> RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (RevokeThrows)
        {
            throw new HttpRequestException("unreachable");
        }

        Revoked.Add(refreshToken);
        return Task.FromResult(OAuthRevocation.Revoked);
    }
}

internal sealed class InMemoryConnectedAccounts : IConnectedAccountDataFactory, IConnectedAccountData
{
    public List<ConnectedAccount> Accounts { get; } = [];

    public List<OAuthRequest> Requests { get; } = [];

    public Task<IConnectedAccountData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IConnectedAccountData>(this);

    public Task<IReadOnlyList<ConnectedAccount>> AccountsOfAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConnectedAccount>>([.. Accounts.Where(account => account.UserId == userId).OrderBy(account => account.ConnectedAt)]);

    public Task<ConnectedAccount?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.SingleOrDefault(account => account.Id == id && account.UserId == userId));

    public Task<ConnectedAccount?> FindByAddressAsync(Guid userId, ConnectedAccountProvider provider, string address, CancellationToken cancellationToken) =>
        Task.FromResult(Accounts.SingleOrDefault(account => account.UserId == userId && account.Provider == provider && account.Address == address));

    public void Add(ConnectedAccount account) => Accounts.Add(account);

    public void Remove(ConnectedAccount account) => Accounts.Remove(account);

    public Task<OAuthRequest?> FindRequestAsync(string stateHash, CancellationToken cancellationToken) =>
        Task.FromResult(Requests.SingleOrDefault(request => request.StateHash == stateHash));

    public void Add(OAuthRequest request) => Requests.Add(request);

    public Task DeleteExpiredRequestsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        Requests.RemoveAll(request => request.ExpiresAt < now);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
