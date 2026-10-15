using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Mailbox;

/// <summary>Why a pending OAuth request cannot be completed (logged as a security event).</summary>
public enum OAuthRequestRejection
{
    None,
    OtherUser,
    OtherProvider,
    Expired,
    AlreadyUsed,
}

/// <summary>
/// A pending "Sign in with Google / Microsoft" of one user (<c>mailbox.oauth_requests</c>, ADR 0021): the hash of the
/// <c>state</c> sent to the provider, the capabilities asked and the PKCE verifier protected server-side (it never
/// reaches the browser). Single use, valid for <see cref="Lifetime"/>.
/// </summary>
public sealed class OAuthRequest : AggregateRoot<Guid>
{
    public const int StateHashLength = 64;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private OAuthRequest(
        Guid id, string stateHash, Guid userId, ConnectedAccountProvider provider, bool wantsMail, bool wantsCalendar, string protectedVerifier, DateTimeOffset now)
        : base(id)
    {
        StateHash = stateHash;
        UserId = userId;
        Provider = provider;
        WantsMail = wantsMail;
        WantsCalendar = wantsCalendar;
        ProtectedVerifier = protectedVerifier;
        CreatedAt = now;
        ExpiresAt = now + Lifetime;
    }

    private OAuthRequest()
    {
        StateHash = string.Empty;
        ProtectedVerifier = string.Empty;
    }

    /// <summary>SHA-256 of the state, lower-case hex.</summary>
    public string StateHash { get; private set; }

    public Guid UserId { get; private set; }

    public ConnectedAccountProvider Provider { get; private set; }

    public bool WantsMail { get; private set; }

    public bool WantsCalendar { get; private set; }

    public string ProtectedVerifier { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public static OAuthRequest Create(
        Guid id, string stateHash, Guid userId, ConnectedAccountProvider provider, bool wantsMail, bool wantsCalendar, string protectedVerifier, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(stateHash);
        ArgumentOutOfRangeException.ThrowIfNotEqual(stateHash.Length, StateHashLength);
        ArgumentException.ThrowIfNullOrEmpty(protectedVerifier);
        if (provider == ConnectedAccountProvider.Imap)
        {
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "IMAP has no OAuth.");
        }

        if (!wantsMail && !wantsCalendar)
        {
            throw new ArgumentException("At least one capability is asked.", nameof(wantsMail));
        }

        return new OAuthRequest(id, stateHash, userId, provider, wantsMail, wantsCalendar, protectedVerifier, now);
    }

    /// <summary>Marks the request used when it belongs to this user and provider and is still valid; it can never be used twice.</summary>
    public OAuthRequestRejection Consume(Guid userId, ConnectedAccountProvider provider, DateTimeOffset now)
    {
        if (UsedAt is not null)
        {
            return OAuthRequestRejection.AlreadyUsed;
        }

        if (userId != UserId)
        {
            return OAuthRequestRejection.OtherUser;
        }

        // Used even when refused below: a state that reached the callback is never accepted again.
        UsedAt = now;
        if (provider != Provider)
        {
            return OAuthRequestRejection.OtherProvider;
        }

        return now >= ExpiresAt ? OAuthRequestRejection.Expired : OAuthRequestRejection.None;
    }
}
