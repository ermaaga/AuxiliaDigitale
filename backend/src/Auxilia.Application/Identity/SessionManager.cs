using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Identity;

/// <summary>
/// Sign-in sessions and tokens (F01, F17, skill auxilia-security): password sign-in on a client application, rotating
/// refresh tokens with reuse detection, logout and revocation (deny-list of the session's access tokens). A session
/// ends when the user's security stamp changes (password, roles, deactivation). Single session per user is the tenant
/// setting <c>auth.singleSession</c> (D-08).
/// </summary>
public interface ISessionManager
{
    Task<Result<TokenPair>> SignInAsync(PasswordSignIn request, CancellationToken cancellationToken);

    Task<Result<TokenPair>> RefreshAsync(RefreshTokens request, CancellationToken cancellationToken);

    /// <summary>Ends a session (logout, administrator revocation) and revokes its access tokens.</summary>
    Task<Result> EndSessionAsync(Guid sessionId, SessionEndReason reason, CancellationToken cancellationToken);
}

public sealed record ClientCredentials(string ClientId, string? ClientSecret);

public sealed record PasswordSignIn(ClientCredentials Client, string UserName, string Password, string? IpAddress, string? UserAgent);

public sealed record RefreshTokens(ClientCredentials Client, string RefreshToken, string? IpAddress, string? UserAgent);

public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, Guid SessionId);

internal sealed class SessionManager : ISessionManager
{
    private readonly IOperationRunner operations;
    private readonly ISessionDataFactory data;
    private readonly IPasswordAuthenticator authenticator;
    private readonly ClientApplicationValidator clients;
    private readonly IAccessTokenIssuer tokens;
    private readonly IAccessTokenDenyList denyList;
    private readonly ISettingsProvider settings;
    private readonly ITenantContext tenantContext;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SessionManager> logger;

    public SessionManager(
        IOperationRunner operations,
        ISessionDataFactory data,
        IPasswordAuthenticator authenticator,
        ClientApplicationValidator clients,
        IAccessTokenIssuer tokens,
        IAccessTokenDenyList denyList,
        ISettingsProvider settings,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        ILogger<SessionManager> logger)
    {
        this.operations = operations;
        this.data = data;
        this.authenticator = authenticator;
        this.clients = clients;
        this.tokens = tokens;
        this.denyList = denyList;
        this.settings = settings;
        this.tenantContext = tenantContext;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public Task<Result<TokenPair>> SignInAsync(PasswordSignIn request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Identity.SignIn, new { request.Client.ClientId }, async scope =>
        {
            if (!await clients.ValidateAsync(request.Client, cancellationToken))
            {
                return Errors.Identity.ClientInvalid();
            }

            var authenticated = await authenticator.AuthenticateAsync(request.UserName, request.Password, cancellationToken);
            if (authenticated.IsFailure)
            {
                return Result.Failure<TokenPair>(authenticated.Error!);
            }

            var user = authenticated.Value;
            scope.SetEntity("User", user.UserId);
            var now = timeProvider.GetUtcNow();
            await using var store = await data.OpenAsync(cancellationToken);

            if (await settings.GetAsync(IdentitySettings.SingleSession, cancellationToken))
            {
                foreach (var other in await store.OpenSessionsOfUserAsync(user.UserId, cancellationToken))
                {
                    await EndAsync(other, SessionEndReason.SingleSession, now, cancellationToken);
                }
            }

            var idle = TimeSpan.FromMinutes(await settings.GetAsync(IdentitySettings.SessionIdleMinutes, cancellationToken));
            var absolute = TimeSpan.FromDays(await settings.GetAsync(IdentitySettings.SessionAbsoluteDays, cancellationToken));
            var session = new RefreshSession(Guid.CreateVersion7(), user.UserId, request.Client.ClientId, user.SecurityStamp, now, now + idle, now + absolute, request.IpAddress, request.UserAgent);
            store.Add(session);
            scope.SetEntity("Session", session.Id);

            var pair = await IssueAsync(store, session, user.UserId, user.Roles, now, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success(pair);
        }, cancellationToken);
    }

    public Task<Result<TokenPair>> RefreshAsync(RefreshTokens request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Identity.RefreshTokens, new { request.Client.ClientId }, async scope =>
        {
            if (!await clients.ValidateAsync(request.Client, cancellationToken))
            {
                return Errors.Identity.ClientInvalid();
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var token = string.IsNullOrWhiteSpace(request.RefreshToken) ? null : await store.FindRefreshTokenAsync(SecureTokens.Hash(request.RefreshToken), cancellationToken);
            var session = token is null ? null : await store.FindSessionAsync(token.SessionId, cancellationToken);
            if (token is null || session is null || !string.Equals(session.ClientId, request.Client.ClientId, StringComparison.Ordinal))
            {
                return Errors.Identity.RefreshTokenInvalid();
            }

            scope.SetEntity("Session", session.Id);
            var now = timeProvider.GetUtcNow();
            if (token.IsConsumed)
            {
                // A rotated token presented again: someone else may hold the family. Revoke it all.
                Log.Security.RefreshTokenReuse(logger, session.Id, session.UserId);
                await EndAsync(session, SessionEndReason.RefreshTokenReuse, now, cancellationToken);
                await store.SaveChangesAsync(cancellationToken);
                return Errors.Identity.RefreshTokenInvalid();
            }

            if (!session.IsActiveAt(now))
            {
                return Errors.Identity.RefreshTokenInvalid();
            }

            var user = await store.FindUserAsync(session.UserId, cancellationToken);
            if (user is null || !user.IsActive || !string.Equals(user.SecurityStamp, session.SecurityStamp, StringComparison.Ordinal))
            {
                await EndAsync(session, SessionEndReason.SecurityStampChanged, now, cancellationToken);
                await store.SaveChangesAsync(cancellationToken);
                return Errors.Identity.RefreshTokenInvalid();
            }

            token.Consume(now);
            session.Touch(now, TimeSpan.FromMinutes(await settings.GetAsync(IdentitySettings.SessionIdleMinutes, cancellationToken)));
            var pair = await IssueAsync(store, session, user.Id, user.Roles, now, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success(pair);
        }, cancellationToken);
    }

    public Task<Result> EndSessionAsync(Guid sessionId, SessionEndReason reason, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.EndSession, new { SessionId = sessionId, Reason = reason.ToString() }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindSessionAsync(sessionId, cancellationToken) is not { } session)
            {
                return Errors.Identity.RefreshTokenInvalid();
            }

            await EndAsync(session, reason, timeProvider.GetUtcNow(), cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    /// <summary>Ends the session and deny-lists its access tokens until the longest one expires.</summary>
    private async Task EndAsync(RefreshSession session, SessionEndReason reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (session.EndedAt is not null)
        {
            return;
        }

        session.End(now, reason);
        var lifetime = TimeSpan.FromMinutes(await settings.GetAsync(IdentitySettings.AccessTokenMinutes, cancellationToken));
        await denyList.DenySessionAsync(tenantContext.Tenant.Slug, session.Id, now + lifetime, cancellationToken);
        var reasonName = Enum.GetName(reason) ?? "Unknown";
        Log.Security.SessionEnded(logger, session.Id, session.UserId, reasonName);
    }

    private async Task<TokenPair> IssueAsync(
        ISessionData store, RefreshSession session, Guid userId, IReadOnlyCollection<SharedKernel.Tenancy.TenantRole> roles, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var refresh = SecureTokens.New();
        store.Add(new RefreshToken(SecureTokens.Hash(refresh), session.Id, now));

        var lifetime = TimeSpan.FromMinutes(await settings.GetAsync(IdentitySettings.AccessTokenMinutes, cancellationToken));
        var access = await tokens.IssueAsync(new AccessTokenRequest(userId, tenantContext.Tenant.Slug, session.Id, session.ClientId, roles, lifetime), cancellationToken);
        return new TokenPair(access.Token, access.ExpiresAt, refresh, session.Id);
    }
}
