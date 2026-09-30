namespace Auxilia.Domain.Platform;

/// <summary>
/// A key of the token signing ring (<c>catalog.signing_keys</c>, skill auxilia-security): ES256, identified by
/// <see cref="Id"/> (<c>kid</c>). The newest non-retired key signs; retired keys stay published in the JWKS while tokens
/// they signed can still be valid (<see cref="PublishedUntil"/>). The private key is stored protected (Data Protection).
/// </summary>
public sealed class SigningKey
{
    public const int IdLength = 32;
    public const string Es256 = "ES256";

    public SigningKey(string id, string publicJwk, string privateKeyProtected, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(id.Length, IdLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicJwk);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyProtected);

        Id = id;
        Algorithm = Es256;
        PublicJwk = publicJwk;
        PrivateKeyProtected = privateKeyProtected;
        CreatedAt = createdAt;
    }

    private SigningKey()
    {
        Id = Algorithm = PublicJwk = PrivateKeyProtected = string.Empty;
    }

    public string Id { get; private set; }

    public string Algorithm { get; private set; }

    /// <summary>Public key as a JWK (JSON), published at <c>/.well-known/jwks.json</c>.</summary>
    public string PublicJwk { get; private set; }

    public string PrivateKeyProtected { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When it stopped signing (a newer key took over); null for the active key.</summary>
    public DateTimeOffset? RetiredAt { get; private set; }

    /// <summary>Last instant the key validates tokens (retirement + the longest token lifetime).</summary>
    public DateTimeOffset? PublishedUntil { get; private set; }

    public bool IsActive => RetiredAt is null;

    public bool IsPublishedAt(DateTimeOffset instant) => PublishedUntil is null || instant < PublishedUntil;

    public void Retire(DateTimeOffset at, TimeSpan validationGrace)
    {
        if (RetiredAt is not null)
        {
            return;
        }

        RetiredAt = at;
        PublishedUntil = at + validationGrace;
    }
}
