using System.Security.Cryptography;
using System.Text.Json;

using Auxilia.Application.Abstractions.Identity;
using Auxilia.Domain.Platform;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Auxilia.Infrastructure.Security.Tokens;

/// <summary>New ES256 (P-256) key pairs: public part as JWK, private part PKCS#8 protected with Data Protection.</summary>
internal sealed class SigningKeyFactory(ISigningKeyProtector protector) : ISigningKeyFactory
{
    public SigningKey Create(DateTimeOffset createdAt)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var id = Guid.NewGuid().ToString("N");
        var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(ecdsa) { KeyId = id });
        jwk.Use = JsonWebKeyUseNames.Sig;
        jwk.Alg = SecurityAlgorithms.EcdsaSha256;

        var publicJwk = JsonSerializer.Serialize(new { kty = jwk.Kty, use = jwk.Use, alg = jwk.Alg, kid = jwk.Kid, crv = jwk.Crv, x = jwk.X, y = jwk.Y });
        var privateKey = Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey());
        return new SigningKey(id, publicJwk, protector.Protect(privateKey), createdAt);
    }
}

/// <summary>
/// The signing ring loaded from the Catalog, cached in memory on each node for <see cref="RefreshInterval"/> (or until a
/// token with an unknown <c>kid</c> arrives). Creates the first key when the ring is empty. Rotation: <c>auxctl keys
/// rotate</c>; other nodes sign with the new key at their next refresh, and keep validating the old one.
/// </summary>
public sealed class SigningKeyRing : IDisposable
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory scopes;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim gate = new(1, 1);
    private volatile Snapshot? snapshot;

    public SigningKeyRing(IServiceScopeFactory scopes, TimeProvider timeProvider)
    {
        this.scopes = scopes;
        this.timeProvider = timeProvider;
    }

    /// <summary>Keys valid for validation (loaded by <see cref="EnsureLoadedAsync"/>).</summary>
    public IReadOnlyList<SecurityKey> ValidationKeys => snapshot?.ValidationKeys ?? [];

    public async Task<SigningCredentials> GetSigningCredentialsAsync(CancellationToken cancellationToken) =>
        (await LoadAsync(unknownKid: null, cancellationToken)).Signing;

    /// <summary>Public keys for <c>/.well-known/jwks.json</c>.</summary>
    public async Task<IReadOnlyList<JsonElement>> GetPublicJwksAsync(CancellationToken cancellationToken) =>
        (await LoadAsync(unknownKid: null, cancellationToken)).PublicJwks;

    /// <summary>Loads the ring when stale, or when <paramref name="forceIfUnknownKid"/> is not in it (rotation on another node).</summary>
    public async Task EnsureLoadedAsync(string? forceIfUnknownKid, CancellationToken cancellationToken) =>
        _ = await LoadAsync(forceIfUnknownKid, cancellationToken);

    private async Task<Snapshot> LoadAsync(string? unknownKid, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (snapshot is { } current && !current.IsStale(now) && (unknownKid is null || current.KeyIds.Contains(unknownKid) || !current.MayReload(now)))
        {
            return current;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (snapshot is { } fresh && !fresh.IsStale(now) && (unknownKid is null || fresh.KeyIds.Contains(unknownKid) || !fresh.MayReload(now)))
            {
                return fresh;
            }

            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ISigningKeyStore>();
            var keys = await store.ListAsync(cancellationToken);
            if (!keys.Any(key => key.IsActive))
            {
                store.Add(scope.ServiceProvider.GetRequiredService<ISigningKeyFactory>().Create(now));
                try
                {
                    await store.SaveChangesAsync(cancellationToken);
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException)
                {
                    // Another node created the first key at the same time (unique active key): use it.
                }

                await using var reload = scopes.CreateAsyncScope();
                keys = await reload.ServiceProvider.GetRequiredService<ISigningKeyStore>().ListAsync(cancellationToken);
            }

            var protector = scope.ServiceProvider.GetRequiredService<ISigningKeyProtector>();
            snapshot = Snapshot.From(keys, protector, now);
            return snapshot;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    private sealed record Snapshot(
        SigningCredentials Signing, IReadOnlyList<SecurityKey> ValidationKeys, IReadOnlyList<JsonElement> PublicJwks, IReadOnlySet<string> KeyIds, DateTimeOffset LoadedAt)
    {
        /// <summary>Unknown kids reload at most every few seconds (no reload storm from forged tokens).</summary>
        private static readonly TimeSpan MinimumReloadInterval = TimeSpan.FromSeconds(10);

        public bool IsStale(DateTimeOffset now) => now - LoadedAt >= RefreshInterval;

        public bool MayReload(DateTimeOffset now) => now - LoadedAt >= MinimumReloadInterval;

        public static Snapshot From(IReadOnlyList<SigningKey> keys, ISigningKeyProtector protector, DateTimeOffset now)
        {
            var published = keys.Where(key => key.IsPublishedAt(now)).ToArray();
            var active = published.Where(key => key.IsActive).OrderByDescending(key => key.CreatedAt).First();

            var validation = published.Select(key => (SecurityKey)PublicKey(key)).ToArray();
            var signing = new SigningCredentials(PrivateKey(active, protector), SecurityAlgorithms.EcdsaSha256);
            var jwks = published.Select(key => JsonDocument.Parse(key.PublicJwk).RootElement.Clone()).ToArray();
            return new Snapshot(signing, validation, jwks, published.Select(key => key.Id).ToHashSet(StringComparer.Ordinal), now);
        }

        private static JsonWebKey PublicKey(SigningKey key) => new(key.PublicJwk);

        private static ECDsaSecurityKey PrivateKey(SigningKey key, ISigningKeyProtector protector)
        {
            var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(protector.Unprotect(key.PrivateKeyProtected)), out _);
            return new ECDsaSecurityKey(ecdsa) { KeyId = key.Id };
        }
    }
}
