using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Identity;

/// <summary>Why a session ended (F17).</summary>
public enum SessionEndReason
{
    Logout,

    /// <summary>A rotated refresh token was used again: the whole session is revoked (skill auxilia-security).</summary>
    RefreshTokenReuse,

    /// <summary>Password, roles or activation changed (security stamp).</summary>
    SecurityStampChanged,

    /// <summary>A new sign-in with single session enabled (D-08).</summary>
    SingleSession,

    /// <summary>Ended by an administrator (F17, B-20).</summary>
    Revoked,
}

/// <summary>
/// A sign-in session of a user on a client application (<c>identity.refresh_sessions</c>): the family of its rotating
/// refresh tokens. Idle expiry slides with every refresh; the absolute expiry never moves. The user's security stamp at
/// sign-in is kept: a different stamp at refresh ends the session.
/// </summary>
public sealed class RefreshSession : AggregateRoot<Guid>
{
    public const int ClientIdMaxLength = 100;
    public const int IpMaxLength = 64;
    public const int UserAgentMaxLength = 500;

    public RefreshSession(
        Guid id,
        Guid userId,
        string clientId,
        string securityStamp,
        DateTimeOffset createdAt,
        DateTimeOffset idleExpiresAt,
        DateTimeOffset absoluteExpiresAt,
        string? ipAddress,
        string? userAgent)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);

        UserId = userId;
        ClientId = clientId;
        SecurityStamp = securityStamp;
        CreatedAt = createdAt;
        LastUsedAt = createdAt;
        IdleExpiresAt = idleExpiresAt < absoluteExpiresAt ? idleExpiresAt : absoluteExpiresAt;
        AbsoluteExpiresAt = absoluteExpiresAt;
        IpAddress = Truncate(ipAddress, IpMaxLength);
        UserAgent = Truncate(userAgent, UserAgentMaxLength);
    }

    private RefreshSession()
    {
        ClientId = SecurityStamp = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string ClientId { get; private set; }

    public string SecurityStamp { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastUsedAt { get; private set; }

    public DateTimeOffset IdleExpiresAt { get; private set; }

    public DateTimeOffset AbsoluteExpiresAt { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTimeOffset? EndedAt { get; private set; }

    public SessionEndReason? EndReason { get; private set; }

    public bool IsActiveAt(DateTimeOffset now) => EndedAt is null && now < IdleExpiresAt && now < AbsoluteExpiresAt;

    /// <summary>A refresh: slides the idle expiry (never beyond the absolute one).</summary>
    public void Touch(DateTimeOffset now, TimeSpan idleTimeout)
    {
        LastUsedAt = now;
        var idle = now + idleTimeout;
        IdleExpiresAt = idle < AbsoluteExpiresAt ? idle : AbsoluteExpiresAt;
    }

    public void End(DateTimeOffset at, SessionEndReason reason)
    {
        if (EndedAt is not null)
        {
            return;
        }

        EndedAt = at;
        EndReason = reason;
    }

    private static string? Truncate(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length > length ? value[..length] : value;
}

/// <summary>
/// A refresh token of a session, stored as SHA-256 (<c>identity.refresh_tokens</c>). Each use consumes it and issues the
/// next one; presenting a consumed token again is a reuse.
/// </summary>
public sealed class RefreshToken
{
    public const int HashLength = 64;

    public RefreshToken(string tokenHash, Guid sessionId, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        TokenHash = tokenHash;
        SessionId = sessionId;
        CreatedAt = createdAt;
    }

    private RefreshToken()
    {
        TokenHash = string.Empty;
    }

    public string TokenHash { get; private set; }

    public Guid SessionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public bool IsConsumed => ConsumedAt is not null;

    public void Consume(DateTimeOffset at) => ConsumedAt ??= at;
}

/// <summary>What a one-use user token is for (<c>identity.user_tokens</c>).</summary>
public enum UserTokenPurpose
{
    /// <summary>Account activation link (D-06).</summary>
    Activation,

    PasswordReset,
}

/// <summary>A one-use token sent by e-mail (activation, password reset), stored as SHA-256 with a short lifetime.</summary>
public sealed class UserToken : Entity<Guid>
{
    public UserToken(Guid id, Guid userId, UserTokenPurpose purpose, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        UserId = userId;
        Purpose = purpose;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    private UserToken()
    {
        TokenHash = string.Empty;
    }

    public Guid UserId { get; private set; }

    public UserTokenPurpose Purpose { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsUsableAt(DateTimeOffset now) => UsedAt is null && now < ExpiresAt;

    public void Use(DateTimeOffset at) => UsedAt ??= at;
}
