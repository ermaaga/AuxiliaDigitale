using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Abstractions.Identity;

/// <summary>Issues signed access tokens (JWT ES256, skill auxilia-security). Permissions are not in the token.</summary>
public interface IAccessTokenIssuer
{
    Task<IssuedAccessToken> IssueAsync(AccessTokenRequest request, CancellationToken cancellationToken);
}

/// <param name="SessionId">The refresh session (<c>sid</c> claim): revoking it revokes the access tokens it issued.</param>
public sealed record AccessTokenRequest(Guid UserId, string TenantSlug, Guid SessionId, string ClientId, IReadOnlyCollection<TenantRole> Roles, TimeSpan Lifetime);

public sealed record IssuedAccessToken(string Token, string TokenId, DateTimeOffset ExpiresAt);

/// <summary>
/// Revoked access tokens until they expire (<c>t:{slug}:auth:deny:{jti}</c>) and revoked sessions until their last
/// access token expires (<c>t:{slug}:auth:deny-sid:{sid}</c>). Checked on every authenticated request.
/// </summary>
public interface IAccessTokenDenyList
{
    Task DenyTokenAsync(string tenantSlug, string tokenId, DateTimeOffset until, CancellationToken cancellationToken);

    Task DenySessionAsync(string tenantSlug, Guid sessionId, DateTimeOffset until, CancellationToken cancellationToken);

    Task<bool> IsDeniedAsync(string tenantSlug, string tokenId, Guid? sessionId, CancellationToken cancellationToken);
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
