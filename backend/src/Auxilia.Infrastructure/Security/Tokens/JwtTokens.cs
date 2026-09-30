using System.Security.Claims;

using Auxilia.Application.Abstractions.Identity;

using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Auxilia.Infrastructure.Security.Tokens;

/// <summary>Access tokens: JWT signed ES256 with the active key of the ring (<c>kid</c> in the header).</summary>
internal sealed class JwtAccessTokenIssuer(SigningKeyRing ring, IOptions<TokenOptions> options, TimeProvider timeProvider) : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler Handler = new();

    public async Task<IssuedAccessToken> IssueAsync(AccessTokenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = timeProvider.GetUtcNow();
        var expiresAt = now + request.Lifetime;
        var tokenId = Guid.NewGuid().ToString("N");
        var claims = new List<Claim>
        {
            new(TokenClaims.Subject, request.UserId.ToString()),
            new(TokenClaims.Tenant, request.TenantSlug),
            new(TokenClaims.Session, request.SessionId.ToString()),
            new(TokenClaims.Client, request.ClientId),
            new(TokenClaims.TokenId, tokenId),
        };
        claims.AddRange(request.Roles.Select(role => new Claim(TokenClaims.Role, role.ToString())));

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = await ring.GetSigningCredentialsAsync(cancellationToken),
        });

        return new IssuedAccessToken(token, tokenId, expiresAt);
    }
}

/// <summary>
/// Deny-list on the distributed cache (Redis when configured, keys <c>t:{slug}:auth:deny…</c> with TTL). Without Redis
/// it is per node: a multi-node deployment needs Redis for immediate revocation. With Redis down the circuit breaker
/// makes lookups misses (fail-open, as the cache): revoked tokens stay usable until they expire (≤ access token lifetime).
/// </summary>
internal sealed class DistributedAccessTokenDenyList(IDistributedCache cache, TimeProvider timeProvider) : IAccessTokenDenyList
{
    private static readonly byte[] Marker = [1];

    public Task DenyTokenAsync(string tenantSlug, string tokenId, DateTimeOffset until, CancellationToken cancellationToken) =>
        SetAsync(TokenKey(tenantSlug, tokenId), until, cancellationToken);

    public Task DenySessionAsync(string tenantSlug, Guid sessionId, DateTimeOffset until, CancellationToken cancellationToken) =>
        SetAsync(SessionKey(tenantSlug, sessionId), until, cancellationToken);

    public async Task<bool> IsDeniedAsync(string tenantSlug, string tokenId, Guid? sessionId, CancellationToken cancellationToken) =>
        await cache.GetAsync(TokenKey(tenantSlug, tokenId), cancellationToken) is not null
        || (sessionId is { } sid && await cache.GetAsync(SessionKey(tenantSlug, sid), cancellationToken) is not null);

    private Task SetAsync(string key, DateTimeOffset until, CancellationToken cancellationToken)
    {
        var ttl = until - timeProvider.GetUtcNow();
        return ttl <= TimeSpan.Zero
            ? Task.CompletedTask
            : cache.SetAsync(key, Marker, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);
    }

    private static string TokenKey(string slug, string tokenId) => $"t:{slug}:auth:deny:{tokenId}";

    private static string SessionKey(string slug, Guid sessionId) => $"t:{slug}:auth:deny-sid:{sessionId:N}";
}
