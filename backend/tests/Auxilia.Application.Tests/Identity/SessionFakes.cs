using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Messaging.Public;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

using NSubstitute;

namespace Auxilia.Application.Tests.Identity;

/// <summary>Sessions and user tokens in memory, over the users of an <see cref="InMemoryIdentityData"/>.</summary>
internal sealed class InMemorySessionData(InMemoryIdentityData identity) : ISessionDataFactory, ISessionData
{
    public List<RefreshSession> Sessions { get; } = [];

    public List<RefreshToken> RefreshTokens { get; } = [];

    public List<UserToken> UserTokens { get; } = [];

    public Task<ISessionData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<ISessionData>(this);

    public Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) => identity.FindAsync(userId, cancellationToken);

    public Task<User?> FindUserByUserNameAsync(string userName, CancellationToken cancellationToken) =>
        identity.FindByUserNameAsync(userName, cancellationToken);

    public Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(RefreshTokens.SingleOrDefault(token => token.TokenHash == tokenHash));

    public Task<RefreshSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Sessions.SingleOrDefault(session => session.Id == sessionId));

    public Task<IReadOnlyList<RefreshSession>> OpenSessionsOfUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RefreshSession>>(Sessions.Where(session => session.UserId == userId && session.EndedAt is null).ToArray());

    public Task<UserToken?> FindUserTokenAsync(string tokenHash, UserTokenPurpose purpose, CancellationToken cancellationToken) =>
        Task.FromResult(UserTokens.SingleOrDefault(token => token.TokenHash == tokenHash && token.Purpose == purpose));

    public Task<IReadOnlyList<UserToken>> UnusedUserTokensAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserToken>>(UserTokens.Where(token => token.UserId == userId && token.Purpose == purpose && token.UsedAt is null).ToArray());

    public void Add(RefreshSession session) => Sessions.Add(session);

    public void Add(RefreshToken token) => RefreshTokens.Add(token);

    public void Add(UserToken token) => UserTokens.Add(token);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class InMemoryClientStore : IClientApplicationStore
{
    public List<ClientApplication> Clients { get; } = [];

    public Task<ClientApplication?> FindAsync(string clientId, CancellationToken cancellationToken) =>
        Task.FromResult(Clients.SingleOrDefault(client => client.ClientId == clientId));

    public Task<IReadOnlyList<ClientApplication>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ClientApplication>>(Clients.ToArray());

    public void Add(ClientApplication application) => Clients.Add(application);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Access tokens are "at:{session}:{n}"; the requests are recorded.</summary>
internal sealed class FakeAccessTokenIssuer(TimeProvider timeProvider) : IAccessTokenIssuer
{
    public List<AccessTokenRequest> Requests { get; } = [];

    public Task<IssuedAccessToken> IssueAsync(AccessTokenRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(new IssuedAccessToken($"at:{request.SessionId}:{Requests.Count}", Guid.NewGuid().ToString("N"), timeProvider.GetUtcNow() + request.Lifetime));
    }
}

internal sealed class InMemoryDenyList : IAccessTokenDenyList
{
    public Dictionary<string, DateTimeOffset> Tokens { get; } = [];

    public Dictionary<Guid, DateTimeOffset> Sessions { get; } = [];

    public Task DenyTokenAsync(string tenantSlug, string tokenId, DateTimeOffset until, CancellationToken cancellationToken)
    {
        Tokens[tokenId] = until;
        return Task.CompletedTask;
    }

    public Task DenySessionAsync(string tenantSlug, Guid sessionId, DateTimeOffset until, CancellationToken cancellationToken)
    {
        Sessions[sessionId] = until;
        return Task.CompletedTask;
    }

    public Task<bool> IsDeniedAsync(string tenantSlug, string tokenId, Guid? sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(Tokens.ContainsKey(tokenId) || (sessionId is { } sid && Sessions.ContainsKey(sid)));
}

internal sealed class RecordingDispatcher : IMessageDispatcher
{
    public List<OutboundMessageRequest> Requests { get; } = [];

    public Result<Guid>? Outcome { get; set; }

    public Task<Result<Guid>> QueueAsync(OutboundMessageRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Outcome ?? Result.Success(Guid.CreateVersion7()));
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset now = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

internal static class SessionSettings
{
    /// <summary>Defaults of every setting type, <c>auth.singleSession</c> as given.</summary>
    public static ISettingsProvider Create(bool singleSession = false)
    {
        var settings = DefaultSettings.Create();
        settings.GetAsync(Arg.Any<SettingDefinition<bool>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SettingDefinition<bool>>().Key == "auth.singleSession" ? singleSession : call.Arg<SettingDefinition<bool>>().Default);
        settings.GetAsync(Arg.Any<SettingDefinition<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<SettingDefinition<string>>().Default);
        return settings;
    }

    public static ITenantContext Tenant()
    {
        var context = Substitute.For<ITenantContext>();
        var tenant = new TenantInfo(Guid.CreateVersion7(), "acme", TenantStatus.Active, "it", "Europe/Rome");
        context.Current.Returns(tenant);
        context.Tenant.Returns(tenant);
        return context;
    }
}
