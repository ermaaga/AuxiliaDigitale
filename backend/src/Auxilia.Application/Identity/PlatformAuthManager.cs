using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Identity;

/// <summary>
/// Console sign-in of platform (System) users (N02, D-22): activation with TOTP enrolment, password + TOTP sign-in on a
/// <c>PlatformConsole</c> client, rotating refresh tokens with reuse detection, logout, and tenant-scoped platform
/// tokens for the technical endpoints of one tenant (every use is a security event, D-21 keeps business data out).
/// </summary>
public interface IPlatformAuthManager
{
    /// <summary>Starts TOTP enrolment for an activation token: a new secret, shown once.</summary>
    Task<Result<TotpEnrollment>> BeginEnrollmentAsync(string activationToken, CancellationToken cancellationToken);

    /// <summary>Sets the password and confirms the enrolled secret with a current code; the token is used up.</summary>
    Task<Result> ActivateAsync(string activationToken, string password, string code, CancellationToken cancellationToken);

    Task<Result<TokenPair>> SignInAsync(PlatformSignIn request, CancellationToken cancellationToken);

    Task<Result<TokenPair>> RefreshAsync(RefreshTokens request, CancellationToken cancellationToken);

    Task<Result> EndSessionAsync(Guid sessionId, SessionEndReason reason, CancellationToken cancellationToken);

    /// <summary>A short-lived platform token for one tenant (any status but archived), for the current platform user and session.</summary>
    Task<Result<IssuedAccessToken>> OpenTenantAsync(string tenantSlug, Guid sessionId, string clientId, CancellationToken cancellationToken);
}

/// <param name="Secret">Base32 secret for manual entry.</param>
/// <param name="Uri"><c>otpauth://</c> URI for the QR code.</param>
public sealed record TotpEnrollment(string Secret, string Uri);

public sealed record PlatformSignIn(ClientCredentials Client, string Email, string Password, string Code, string? IpAddress, string? UserAgent);

/// <summary>Ends the console sessions of a platform user (credential reset, deactivation); the caller saves.</summary>
internal interface IPlatformSessionEnder
{
    Task EndAllAsync(Guid platformUserId, SessionEndReason reason, CancellationToken cancellationToken);
}

internal sealed class PlatformAuthManager : IPlatformAuthManager, IPlatformSessionEnder
{
    /// <summary>Verified when the user does not exist, so unknown and known e-mails take the same time.</summary>
    private readonly Lazy<string> dummyHash;

    private readonly IOperationRunner operations;
    private readonly IPlatformIdentityStore store;
    private readonly IPasswordHasher hasher;
    private readonly ITotpService totp;
    private readonly ITwoFactorSecretProtector protector;
    private readonly ClientApplicationValidator clients;
    private readonly IAccessTokenIssuer tokens;
    private readonly IAccessTokenDenyList denyList;
    private readonly ICurrentUser currentUser;
    private readonly ITenantDirectory tenants;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<PlatformAuthManager> logger;

    public PlatformAuthManager(
        IOperationRunner operations,
        IPlatformIdentityStore store,
        IPasswordHasher hasher,
        ITotpService totp,
        ITwoFactorSecretProtector protector,
        ClientApplicationValidator clients,
        IAccessTokenIssuer tokens,
        IAccessTokenDenyList denyList,
        ICurrentUser currentUser,
        ITenantDirectory tenants,
        TimeProvider timeProvider,
        ILogger<PlatformAuthManager> logger)
    {
        this.operations = operations;
        this.store = store;
        this.hasher = hasher;
        this.totp = totp;
        this.protector = protector;
        this.clients = clients;
        this.tokens = tokens;
        this.denyList = denyList;
        this.currentUser = currentUser;
        this.tenants = tenants;
        this.timeProvider = timeProvider;
        this.logger = logger;
        dummyHash = new Lazy<string>(() => hasher.Hash(Guid.NewGuid().ToString()));
    }

    public Task<Result<TotpEnrollment>> BeginEnrollmentAsync(string activationToken, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.BeginPlatformEnrollment, null, async scope =>
        {
            var (_, user) = await FindActivationAsync(activationToken, cancellationToken);
            if (user is null)
            {
                return Errors.Identity.UserTokenInvalid();
            }

            scope.SetEntity("PlatformUser", user.Id);
            var secret = totp.NewSecret();
            user.BeginEnrollment(protector.Protect(secret));
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success(new TotpEnrollment(secret, totp.EnrollmentUri(secret, user.Email, PlatformIdentityPolicy.TotpIssuer)));
        }, cancellationToken);

    public Task<Result> ActivateAsync(string activationToken, string password, string code, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.ActivatePlatformAccount, null, async scope =>
        {
            var (token, user) = await FindActivationAsync(activationToken, cancellationToken);
            if (token is null || user is null)
            {
                return Errors.Identity.UserTokenInvalid();
            }

            scope.SetEntity("PlatformUser", user.Id);
            if (string.IsNullOrEmpty(password) || password.Length < PlatformIdentityPolicy.PasswordMinLength)
            {
                return Errors.Identity.PasswordTooWeak(PlatformIdentityPolicy.PasswordMinLength);
            }

            var step = user.PendingTwoFactorSecret is { } pending ? totp.Verify(protector.Unprotect(pending), code ?? string.Empty, timeProvider.GetUtcNow()) : null;
            if (step is null)
            {
                return Errors.Identity.TwoFactorCodeInvalid();
            }

            user.CompleteEnrollment(hasher.Hash(password), step.Value);
            token.Use(timeProvider.GetUtcNow());
            await store.SaveChangesAsync(cancellationToken);
            Log.Security.PlatformCredentialsChanged(logger, user.Id, "activated");
            return Result.Success();
        }, cancellationToken);

    public Task<Result<TokenPair>> SignInAsync(PlatformSignIn request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Identity.PlatformSignIn, new { request.Client.ClientId }, async scope =>
        {
            if (!await clients.ValidateAsync(request.Client, cancellationToken, platform: true))
            {
                return Errors.Identity.ClientInvalid();
            }

            var user = string.IsNullOrWhiteSpace(request.Email) ? null : await store.FindUserByEmailAsync(request.Email.Trim(), cancellationToken);
            if (user is null)
            {
                hasher.Verify(dummyHash.Value, PasswordFormat.Identity, request.Password ?? string.Empty);
                return Fail("UnknownUser", null);
            }

            scope.SetEntity("PlatformUser", user.Id);
            var now = timeProvider.GetUtcNow();
            if (user.IsLockedOut(now))
            {
                Log.Security.PlatformLoginFailed(logger, "LockedOut", user.Id);
                return Errors.Identity.AccountLocked();
            }

            var passwordOk = user.PasswordHash is { } hash
                && hasher.Verify(hash, PasswordFormat.Identity, request.Password ?? string.Empty) != PasswordVerification.Failed;
            var step = passwordOk && user.TwoFactorSecret is { } secret ? totp.Verify(protector.Unprotect(secret), request.Code ?? string.Empty, now) : null;

            // Password and code fail alike (no hint which one was wrong); a replayed code counts as wrong.
            if (!passwordOk || step is null || !user.TryUseTotpStep(step.Value))
            {
                var reason = !user.IsEnrolled ? "NotEnrolled" : !passwordOk ? "WrongPassword" : "WrongCode";
                var lockedUntil = user.RecordFailedSignIn(now, PlatformIdentityPolicy.LockoutMaxFailedAttempts, PlatformIdentityPolicy.LockoutDuration);
                await store.SaveChangesAsync(cancellationToken);
                if (lockedUntil is { } end)
                {
                    Log.Security.PlatformAccountLockedOut(logger, user.Id, end);
                }

                return Fail(reason, user.Id);
            }

            if (!user.IsActive)
            {
                return Fail("Inactive", user.Id);
            }

            user.RecordSuccessfulSignIn(now);
            var session = new RefreshSession(
                Guid.CreateVersion7(), user.Id, request.Client.ClientId, user.SecurityStamp, now,
                now + PlatformIdentityPolicy.SessionIdle, now + PlatformIdentityPolicy.SessionAbsolute, request.IpAddress, request.UserAgent);
            store.Add(session);
            scope.SetEntity("Session", session.Id);
            var pair = await IssueAsync(session, now, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            Log.Security.PlatformSignedIn(logger, user.Id, session.Id);
            return Result.Success(pair);
        }, cancellationToken);
    }

    public Task<Result<TokenPair>> RefreshAsync(RefreshTokens request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Identity.PlatformRefreshTokens, new { request.Client.ClientId }, async scope =>
        {
            if (!await clients.ValidateAsync(request.Client, cancellationToken, platform: true))
            {
                return Errors.Identity.ClientInvalid();
            }

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
                Log.Security.RefreshTokenReuse(logger, session.Id, session.UserId);
                await EndAsync(session, SessionEndReason.RefreshTokenReuse, now, cancellationToken);
                await store.SaveChangesAsync(cancellationToken);
                return Errors.Identity.RefreshTokenInvalid();
            }

            var user = session.IsActiveAt(now) ? await store.FindUserAsync(session.UserId, cancellationToken) : null;
            if (user is null || !user.IsActive || !string.Equals(user.SecurityStamp, session.SecurityStamp, StringComparison.Ordinal))
            {
                await EndAsync(session, SessionEndReason.SecurityStampChanged, now, cancellationToken);
                await store.SaveChangesAsync(cancellationToken);
                return Errors.Identity.RefreshTokenInvalid();
            }

            token.Consume(now);
            session.Touch(now, PlatformIdentityPolicy.SessionIdle);
            var pair = await IssueAsync(session, now, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success(pair);
        }, cancellationToken);
    }

    public Task<Result> EndSessionAsync(Guid sessionId, SessionEndReason reason, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.EndPlatformSession, new { SessionId = sessionId, Reason = reason.ToString() }, async _ =>
        {
            if (await store.FindSessionAsync(sessionId, cancellationToken) is not { } session)
            {
                return Errors.Identity.RefreshTokenInvalid();
            }

            await EndAsync(session, reason, timeProvider.GetUtcNow(), cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<IssuedAccessToken>> OpenTenantAsync(string tenantSlug, Guid sessionId, string clientId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.IssuePlatformTenantToken, new { TenantSlug = tenantSlug }, async scope =>
        {
            if (currentUser.ActorType != ActorType.Platform || currentUser.UserId is not { } platformUserId)
            {
                return Errors.Identity.PlatformAccessRequired();
            }

            var tenant = string.IsNullOrWhiteSpace(tenantSlug) ? null : await tenants.FindBySlugAsync(tenantSlug.Trim().ToLowerInvariant(), cancellationToken);
            if (tenant is null || tenant.Status == TenantStatus.Archived)
            {
                return Errors.Tenancy.TenantNotFound();
            }

            scope.SetEntity("Tenant", tenant.Id);
            var issued = await tokens.IssuePlatformAsync(
                new PlatformAccessTokenRequest(platformUserId, sessionId, clientId, tenant.Slug, PlatformIdentityPolicy.TenantTokenLifetime), cancellationToken);
            Log.Security.PlatformTenantAccess(logger, platformUserId, tenant.Slug);
            return Result.Success(issued);
        }, cancellationToken);

    public async Task EndAllAsync(Guid platformUserId, SessionEndReason reason, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var session in await store.OpenSessionsOfUserAsync(platformUserId, cancellationToken))
        {
            await EndAsync(session, reason, now, cancellationToken);
        }
    }

    private async Task EndAsync(RefreshSession session, SessionEndReason reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (session.EndedAt is not null)
        {
            return;
        }

        session.End(now, reason);

        // Console and tenant-scoped platform tokens of the session live at most this long.
        var longest = PlatformIdentityPolicy.AccessTokenLifetime > PlatformIdentityPolicy.TenantTokenLifetime
            ? PlatformIdentityPolicy.AccessTokenLifetime
            : PlatformIdentityPolicy.TenantTokenLifetime;
        await denyList.DenySessionAsync(IAccessTokenDenyList.PlatformNamespace, session.Id, now + longest, cancellationToken);
        var reasonName = Enum.GetName(reason) ?? "Unknown";
        Log.Security.SessionEnded(logger, session.Id, session.UserId, reasonName);
    }

    private async Task<TokenPair> IssueAsync(RefreshSession session, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var refresh = SecureTokens.New();
        store.Add(new RefreshToken(SecureTokens.Hash(refresh), session.Id, now));
        var access = await tokens.IssuePlatformAsync(
            new PlatformAccessTokenRequest(session.UserId, session.Id, session.ClientId, null, PlatformIdentityPolicy.AccessTokenLifetime), cancellationToken);
        return new TokenPair(access.Token, access.ExpiresAt, refresh, session.Id);
    }

    private async Task<(UserToken? Token, PlatformUser? User)> FindActivationAsync(string activationToken, CancellationToken cancellationToken)
    {
        var token = string.IsNullOrWhiteSpace(activationToken)
            ? null
            : await store.FindUserTokenAsync(SecureTokens.Hash(activationToken), UserTokenPurpose.Activation, cancellationToken);
        if (token is null || !token.IsUsableAt(timeProvider.GetUtcNow()))
        {
            return (null, null);
        }

        var user = await store.FindUserAsync(token.UserId, cancellationToken);
        return user is { IsActive: true } ? (token, user) : (null, null);
    }

    private Error Fail(string reason, Guid? userId)
    {
        Log.Security.PlatformLoginFailed(logger, reason, userId);
        return Errors.Identity.InvalidCredentials();
    }
}
