using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Configuration.Public;
using Auxilia.Contracts.Realtime;
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
    /// <returns>403 <c>AUX-12043</c> when the password expired (change it with <see cref="ChangeExpiredPasswordAsync"/>).</returns>
    Task<Result<TokenPair>> SignInAsync(PasswordSignIn request, CancellationToken cancellationToken);

    /// <summary>Sign-in with the code e-mailed by <c>IAccountLinkManager.SendLoginOtpAsync</c> (method <c>email-otp</c>, F35).</summary>
    Task<Result<TokenPair>> SignInWithOtpAsync(OtpSignIn request, CancellationToken cancellationToken);

    /// <summary>Changes an expired password with the current one, then signs in (F35).</summary>
    Task<Result<TokenPair>> ChangeExpiredPasswordAsync(ExpiredPasswordChange request, CancellationToken cancellationToken);

    /// <summary>
    /// The user's roles require the authenticator app and none is set (<c>AUX-12074</c>, N04): with user name and password
    /// as the credential a new secret is generated (QR code). Refused when the app is not required or already set.
    /// </summary>
    Task<Result<TotpEnrollment>> BeginRequiredSetupAsync(TwoFactorSetup request, CancellationToken cancellationToken);

    /// <summary>Confirms the enrolment started by <see cref="BeginRequiredSetupAsync"/> with a code of the app, then signs in.</summary>
    Task<Result<TokenPair>> ConfirmRequiredSetupAsync(TwoFactorSetupConfirmation request, CancellationToken cancellationToken);

    /// <summary>
    /// The user changes their own password: the current one is required, the policy and history apply; the calling
    /// session stays signed in, every other session of the user ends (F35).
    /// </summary>
    Task<Result> ChangePasswordAsync(Guid userId, Guid? currentSessionId, string currentPassword, string newPassword, CancellationToken cancellationToken);

    Task<Result<TokenPair>> RefreshAsync(RefreshTokens request, CancellationToken cancellationToken);

    /// <summary>Ends a session (logout, administrator revocation) and revokes its access tokens.</summary>
    Task<Result> EndSessionAsync(Guid sessionId, SessionEndReason reason, CancellationToken cancellationToken);

    /// <summary>
    /// F04 "my sessions": the user ends one of their own open sessions (the current one included); its access tokens are
    /// deny-listed and its connections told to sign out. Another user's session is not found.
    /// </summary>
    Task<Result> EndOwnSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// An Administrator ends an open session of any user of the tenant (F17): its access tokens are denied at once and
    /// its connections receive <c>ForceLogout</c> (reason <c>Revoked</c>).
    /// </summary>
    Task<Result> RevokeAsync(Guid sessionId, Guid? actorUserId, CancellationToken cancellationToken);
}

public sealed record ClientCredentials(string ClientId, string? ClientSecret);

/// <param name="TwoFactorCode">The code of the authenticator app, required when the user has it (N04).</param>
/// <param name="RememberMe">"Stay signed in": the session stays open up to <c>auth.session.rememberMeDays</c> (N04).</param>
public sealed record PasswordSignIn(
    ClientCredentials Client, string UserName, string Password, string? IpAddress, string? UserAgent, string? TwoFactorCode = null, bool RememberMe = false);

public sealed record RefreshTokens(ClientCredentials Client, string RefreshToken, string? IpAddress, string? UserAgent);

public sealed record OtpSignIn(
    ClientCredentials Client, string UserName, string Code, string? IpAddress, string? UserAgent, string? TwoFactorCode = null, bool RememberMe = false);

public sealed record ExpiredPasswordChange(
    ClientCredentials Client, string UserName, string CurrentPassword, string NewPassword, string? IpAddress, string? UserAgent,
    string? TwoFactorCode = null, bool RememberMe = false);

public sealed record TwoFactorSetup(ClientCredentials Client, string UserName, string Password, string? IpAddress, string? UserAgent);

public sealed record TwoFactorSetupConfirmation(
    ClientCredentials Client, string UserName, string Password, string Code, bool RememberMe, string? IpAddress, string? UserAgent);

/// <summary>Sign-in methods (<c>login_attempts.method</c>, <c>GET /auth/methods</c>).</summary>
public static class LoginMethods
{
    public const string Password = "password";
    public const string EmailOtp = "email-otp";
}

/// <param name="RememberedUntil">
/// "Stay signed in" (N04): the session stays open until then without activity, so the web app keeps its cookie as long;
/// null for an ordinary session (the cookie ends with the browser).
/// </param>
public sealed record TokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken, Guid SessionId, DateTimeOffset? RememberedUntil = null);

internal sealed partial class SessionManager : ISessionManager
{
    private readonly IOperationRunner operations;
    private readonly ISessionDataFactory data;
    private readonly IPasswordAuthenticator authenticator;
    private readonly ClientApplicationValidator clients;
    private readonly IAccessTokenIssuer tokens;
    private readonly IAccessTokenDenyList denyList;
    private readonly ISettingsProvider settings;
    private readonly ITenantContext tenantContext;
    private readonly IRealtimeNotifier realtime;
    private readonly IPasswordPolicy passwordPolicy;
    private readonly IPasswordHasher hasher;
    private readonly ITotpService totp;
    private readonly IUserTwoFactorSecretProtector secretProtector;
    private readonly ITenantAppName appName;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<SessionManager> logger;
    private readonly List<(Guid SessionId, SessionEndReason Reason)> endedSessions = [];

    public SessionManager(
        IOperationRunner operations,
        ISessionDataFactory data,
        IPasswordAuthenticator authenticator,
        ClientApplicationValidator clients,
        IAccessTokenIssuer tokens,
        IAccessTokenDenyList denyList,
        ISettingsProvider settings,
        ITenantContext tenantContext,
        IRealtimeNotifier realtime,
        IPasswordPolicy passwordPolicy,
        IPasswordHasher hasher,
        ITotpService totp,
        IUserTwoFactorSecretProtector secretProtector,
        ITenantAppName appName,
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
        this.realtime = realtime;
        this.passwordPolicy = passwordPolicy;
        this.hasher = hasher;
        this.totp = totp;
        this.secretProtector = secretProtector;
        this.appName = appName;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public Task<Result<TokenPair>> SignInAsync(PasswordSignIn request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Identity.SignIn, new { request.Client.ClientId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var attempt = new AttemptInfo(request.UserName, LoginMethods.Password, request.IpAddress, request.UserAgent);
            if (!await clients.ValidateAsync(request.Client, cancellationToken))
            {
                return await FailAsync(store, attempt, null, "ClientInvalid", Errors.Identity.ClientInvalid(), cancellationToken);
            }

            var authenticated = await authenticator.AuthenticateAsync(request.UserName, request.Password, cancellationToken);
            if (authenticated.IsFailure)
            {
                var userId = string.IsNullOrWhiteSpace(request.UserName) ? null : (await store.FindUserByUserNameAsync(request.UserName.Trim(), cancellationToken))?.Id;
                var reason = authenticated.Error!.Code == EventCodes.Identity.AccountLocked ? "LockedOut" : "InvalidCredentials";
                return await FailAsync(store, attempt, userId, reason, authenticated.Error, cancellationToken);
            }

            var user = await store.FindUserAsync(authenticated.Value.UserId, cancellationToken);
            scope.SetEntity("User", authenticated.Value.UserId);
            var now = timeProvider.GetUtcNow();
            if (user is null)
            {
                return await FailAsync(store, attempt, null, "InvalidCredentials", Errors.Identity.InvalidCredentials(), cancellationToken);
            }

            if (await passwordPolicy.IsExpiredAsync(user, now, cancellationToken))
            {
                Log.Security.LoginFailed(logger, "PasswordExpired", user.Id);
                return await FailAsync(store, attempt, user.Id, "PasswordExpired", Errors.Identity.PasswordExpired(), cancellationToken);
            }

            if (await CheckSecondFactorAsync(store, user, request.TwoFactorCode, attempt, now, cancellationToken) is { } refused)
            {
                return refused;
            }

            return Result.Success(await OpenSessionAsync(store, scope, user, request.Client, attempt, request.RememberMe, now, cancellationToken));
        }, cancellationToken);
    }

    /// <summary>
    /// Opens a session for an authenticated user (single session applied), records the successful attempt, issues the
    /// token pair and saves. "Stay signed in" widens the idle window to <c>auth.session.rememberMeDays</c> (0 = off).
    /// </summary>
    private async Task<TokenPair> OpenSessionAsync(
        ISessionData store, IOperationScope scope, User user, ClientCredentials client, AttemptInfo attempt, bool rememberMe, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await settings.GetAsync(IdentitySettings.SingleSession, cancellationToken))
        {
            foreach (var other in await store.OpenSessionsOfUserAsync(user.Id, cancellationToken))
            {
                await EndAsync(other, SessionEndReason.SingleSession, now, cancellationToken);
            }
        }

        var idle = TimeSpan.FromMinutes(await settings.GetAsync(IdentitySettings.SessionIdleMinutes, cancellationToken));
        var absolute = TimeSpan.FromDays(await settings.GetAsync(IdentitySettings.SessionAbsoluteDays, cancellationToken));
        var session = new RefreshSession(Guid.CreateVersion7(), user.Id, client.ClientId, user.SecurityStamp, now, now + idle, now + absolute, attempt.IpAddress, attempt.UserAgent);
        if (rememberMe && await RememberWindowAsync(cancellationToken) is { } window)
        {
            session.Remember(now, window);
        }

        store.Add(session);
        scope.SetEntity("Session", session.Id);
        store.Add(attempt.ToEntity(user.Id, now, succeeded: true, failureReason: null));

        var pair = await IssueAsync(store, session, user.Id, user.Roles, now, cancellationToken);
        await SaveAsync(store, cancellationToken);
        return pair;
    }

    /// <summary>Records the failed attempt (<c>identity.login_attempts</c>, F35) and returns the error.</summary>
    private async Task<Result<TokenPair>> FailAsync(
        ISessionData store, AttemptInfo attempt, Guid? userId, string reason, Error error, CancellationToken cancellationToken)
    {
        store.Add(attempt.ToEntity(userId, timeProvider.GetUtcNow(), succeeded: false, reason));
        await SaveAsync(store, cancellationToken);
        return Result.Failure<TokenPair>(error);
    }

    private sealed record AttemptInfo(string? UserName, string Method, string? IpAddress, string? UserAgent)
    {
        public LoginAttempt ToEntity(Guid? userId, DateTimeOffset at, bool succeeded, string? failureReason) =>
            new(Guid.CreateVersion7(), userId, UserName ?? string.Empty, Method, at, succeeded, failureReason, IpAddress, UserAgent);
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
                await SaveAsync(store, cancellationToken);
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
                await SaveAsync(store, cancellationToken);
                return Errors.Identity.RefreshTokenInvalid();
            }

            token.Consume(now);
            if (session.IsRemembered && await RememberWindowAsync(cancellationToken) is { } window)
            {
                session.Remember(now, window);
            }
            else
            {
                session.Touch(now, TimeSpan.FromMinutes(await settings.GetAsync(IdentitySettings.SessionIdleMinutes, cancellationToken)));
            }

            var pair = await IssueAsync(store, session, user.Id, user.Roles, now, cancellationToken);
            await SaveAsync(store, cancellationToken);
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
            await SaveAsync(store, cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> EndOwnSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.EndOwnSession, new { UserId = userId, SessionId = sessionId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            if (await store.FindSessionAsync(sessionId, cancellationToken) is not { } session
                || session.UserId != userId
                || !session.IsActiveAt(now))
            {
                return Errors.Identity.SessionNotFound();
            }

            await EndAsync(session, SessionEndReason.Logout, now, cancellationToken);
            await SaveAsync(store, cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> RevokeAsync(Guid sessionId, Guid? actorUserId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.RevokeSession, new { SessionId = sessionId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            if (await store.FindSessionAsync(sessionId, cancellationToken) is not { } session || !session.IsActiveAt(now))
            {
                return Errors.Identity.SessionNotFound();
            }

            await EndAsync(session, SessionEndReason.Revoked, now, cancellationToken);
            Log.Security.SessionRevokedByAdministrator(logger, session.Id, session.UserId, actorUserId);
            await SaveAsync(store, cancellationToken);
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
        endedSessions.Add((session.Id, reason));
    }

    /// <summary>Saves, then tells the connected clients of every session ended meanwhile to sign out (F17).</summary>
    private async Task SaveAsync(ISessionData store, CancellationToken cancellationToken)
    {
        await store.SaveChangesAsync(cancellationToken);
        foreach (var (sessionId, reason) in endedSessions)
        {
            await realtime.ToSessionAsync(sessionId, RealtimeEvents.ForceLogout, new ForceLogoutEvent(Enum.GetName(reason) ?? "Unknown"), cancellationToken);
        }

        endedSessions.Clear();
    }

    private async Task<TokenPair> IssueAsync(
        ISessionData store, RefreshSession session, Guid userId, IReadOnlyCollection<SharedKernel.Tenancy.TenantRole> roles, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var refresh = SecureTokens.New();
        store.Add(new RefreshToken(SecureTokens.Hash(refresh), session.Id, now));

        var lifetime = TimeSpan.FromMinutes(await settings.GetAsync(IdentitySettings.AccessTokenMinutes, cancellationToken));
        var access = await tokens.IssueAsync(new AccessTokenRequest(userId, tenantContext.Tenant.Slug, session.Id, session.ClientId, roles, lifetime), cancellationToken);
        return new TokenPair(access.Token, access.ExpiresAt, refresh, session.Id, session.IsRemembered ? session.IdleExpiresAt : null);
    }
}
