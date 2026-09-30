using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Abstractions.Identity;

/// <summary>Issues signed access tokens (JWT ES256, skill auxilia-security). Permissions are not in the token.</summary>
public interface IAccessTokenIssuer
{
    Task<IssuedAccessToken> IssueAsync(AccessTokenRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// A platform (System) token: <c>scope=platform</c>, <c>actor_type=platform</c>, <c>role=System</c>, <c>act</c> = the
    /// platform user; with <see cref="PlatformAccessTokenRequest.TenantSlug"/> also <c>tenant</c> (tenant-scoped token).
    /// </summary>
    Task<IssuedAccessToken> IssuePlatformAsync(PlatformAccessTokenRequest request, CancellationToken cancellationToken);
}

/// <param name="TenantSlug">Null for the console token; the opened tenant for a tenant-scoped platform token.</param>
public sealed record PlatformAccessTokenRequest(Guid PlatformUserId, Guid SessionId, string ClientId, string? TenantSlug, TimeSpan Lifetime);

/// <param name="SessionId">The refresh session (<c>sid</c> claim): revoking it revokes the access tokens it issued.</param>
public sealed record AccessTokenRequest(Guid UserId, string TenantSlug, Guid SessionId, string ClientId, IReadOnlyCollection<TenantRole> Roles, TimeSpan Lifetime);

public sealed record IssuedAccessToken(string Token, string TokenId, DateTimeOffset ExpiresAt);

/// <summary>
/// Revoked access tokens until they expire (<c>t:{slug}:auth:deny:{jti}</c>) and revoked sessions until their last
/// access token expires (<c>t:{slug}:auth:deny-sid:{sid}</c>). Checked on every authenticated request. Platform tokens
/// use <see cref="PlatformNamespace"/> as slug (a reserved slug, never a tenant).
/// </summary>
public interface IAccessTokenDenyList
{
    Task DenyTokenAsync(string tenantSlug, string tokenId, DateTimeOffset until, CancellationToken cancellationToken);

    Task DenySessionAsync(string tenantSlug, Guid sessionId, DateTimeOffset until, CancellationToken cancellationToken);

    Task<bool> IsDeniedAsync(string tenantSlug, string tokenId, Guid? sessionId, CancellationToken cancellationToken);

    public const string PlatformNamespace = "platform";
}

/// <summary>Client applications of the Catalog (<c>X-Client-Id</c>, D-03).</summary>
public interface IClientApplicationStore
{
    Task<ClientApplication?> FindAsync(string clientId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClientApplication>> ListAsync(CancellationToken cancellationToken);

    void Add(ClientApplication application);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>The token signing ring in the Catalog (tracked entities).</summary>
public interface ISigningKeyStore
{
    Task<IReadOnlyList<SigningKey>> ListAsync(CancellationToken cancellationToken);

    void Add(SigningKey key);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Creates a new ES256 key pair as a <see cref="SigningKey"/> (private key already protected).</summary>
public interface ISigningKeyFactory
{
    SigningKey Create(DateTimeOffset createdAt);
}

/// <summary>Sessions, refresh tokens and one-use user tokens of the current tenant (one unit of work).</summary>
public interface ISessionData : IAsyncDisposable
{
    Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<User?> FindUserByUserNameAsync(string userName, CancellationToken cancellationToken);

    Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken);

    Task<RefreshSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefreshSession>> OpenSessionsOfUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserToken?> FindUserTokenAsync(string tokenHash, UserTokenPurpose purpose, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserToken>> UnusedUserTokensAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken);

    void Add(RefreshSession session);

    void Add(RefreshToken token);

    void Add(UserToken token);

    /// <summary>A sign-in attempt for the login audit (F35).</summary>
    void Add(LoginAttempt attempt);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ISessionDataFactory
{
    Task<ISessionData> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>Protects the private keys of the signing ring at rest (Data Protection, Catalog key ring).</summary>
public interface ISigningKeyProtector
{
    string Protect(string privateKey);

    string Unprotect(string protectedPrivateKey);
}

/// <summary>Platform users, their console sessions and activation tokens (Catalog, one unit of work per scope).</summary>
public interface IPlatformIdentityStore
{
    Task<PlatformUser?> FindUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Case-insensitive (citext).</summary>
    Task<PlatformUser?> FindUserByEmailAsync(string email, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlatformUser>> ListUsersAsync(CancellationToken cancellationToken);

    Task<UserToken?> FindUserTokenAsync(string tokenHash, UserTokenPurpose purpose, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserToken>> UnusedUserTokensAsync(Guid userId, UserTokenPurpose purpose, CancellationToken cancellationToken);

    Task<RefreshSession?> FindSessionAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefreshSession>> OpenSessionsOfUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<RefreshToken?> FindRefreshTokenAsync(string tokenHash, CancellationToken cancellationToken);

    void Add(PlatformUser user);

    void Add(UserToken token);

    void Add(RefreshSession session);

    void Add(RefreshToken token);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Protects TOTP secrets at rest (Data Protection, Catalog key ring).</summary>
public interface ITwoFactorSecretProtector
{
    string Protect(string secret);

    string Unprotect(string protectedSecret);
}

/// <summary>Time-based one-time passwords (RFC 6238: HMAC-SHA1, 6 digits, 30-second steps).</summary>
public interface ITotpService
{
    /// <summary>A new random secret, Base32 (160 bits).</summary>
    string NewSecret();

    /// <summary><c>otpauth://totp/…</c> URI for authenticator apps (QR code).</summary>
    string EnrollmentUri(string secret, string accountName, string issuer);

    /// <summary>The time step the code matches (current step ± 1 for clock drift), or null.</summary>
    long? Verify(string secret, string code, DateTimeOffset now);
}

/// <summary>
/// A sign-in method of the tenant app (ARCHITECTURE §6: <c>password</c>, <c>email-otp</c>; future external providers).
/// <c>GET /auth/methods</c> lists the enabled ones.
/// </summary>
public interface IAuthenticationMethod
{
    string Code { get; }

    Task<bool> IsEnabledAsync(CancellationToken cancellationToken);
}

/// <summary>Filters, sort and page of the login audit (F35).</summary>
/// <param name="Sort"><c>attemptedAt</c>, <c>-attemptedAt</c> (default), <c>userName</c>, <c>-userName</c>.</param>
public sealed record LoginAttemptQuery(
    string? UserName, string? Method, bool? Succeeded, DateTimeOffset? From, DateTimeOffset? To, string? Sort, int Page, int PageSize);

/// <summary>Reads <c>identity.login_attempts</c> of the current tenant (untracked, paged in the database).</summary>
public interface ILoginAttemptReader
{
    Task<(IReadOnlyList<LoginAttempt> Items, long TotalCount)> ListAsync(LoginAttemptQuery query, CancellationToken cancellationToken);
}
