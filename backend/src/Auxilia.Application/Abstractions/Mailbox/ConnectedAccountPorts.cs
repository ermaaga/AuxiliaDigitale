using System.Net;

using Auxilia.Domain.Mailbox;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Abstractions.Mailbox;

/// <summary>Connected accounts and pending OAuth requests of the current tenant (<c>mailbox</c> schema, ADR 0021).</summary>
public interface IConnectedAccountData : IAsyncDisposable
{
    /// <summary>The accounts of one user, oldest first.</summary>
    Task<IReadOnlyList<ConnectedAccount>> AccountsOfAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The account only when it belongs to the user (other users' accounts are not visible).</summary>
    Task<ConnectedAccount?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    Task<ConnectedAccount?> FindByAddressAsync(Guid userId, ConnectedAccountProvider provider, string address, CancellationToken cancellationToken);

    void Add(ConnectedAccount account);

    /// <summary>Deletes the account and, by cascade, everything copied from it.</summary>
    void Remove(ConnectedAccount account);

    Task<OAuthRequest?> FindRequestAsync(string stateHash, CancellationToken cancellationToken);

    void Add(OAuthRequest request);

    /// <summary>Deletes the requests expired before <paramref name="now"/> (used or not), right away.</summary>
    Task DeleteExpiredRequestsAsync(DateTimeOffset now, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IConnectedAccountDataFactory
{
    Task<IConnectedAccountData> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>Data Protection of refresh tokens, IMAP passwords and PKCE verifiers; the tenant is part of the purpose.</summary>
public interface IConnectedAccountSecretProtector
{
    string Protect(string secret);

    string Unprotect(string protectedSecret);
}

/// <summary>An OAuth provider of connected accounts (Google, Microsoft): the API is the confidential client (ADR 0021).</summary>
public interface IOAuthProvider
{
    ConnectedAccountProvider Provider { get; }

    /// <summary>False when the deployment has no OAuth application for it: the provider is not offered.</summary>
    bool IsConfigured { get; }

    bool SupportsCalendar { get; }

    /// <summary>The provider's consent page for these capabilities (incremental, read-only scopes), with state and PKCE challenge.</summary>
    Uri AuthorizationUrl(OAuthAuthorization authorization);

    /// <summary>Exchanges the authorization code (with the PKCE verifier); <c>AUX-30011</c> when the provider refuses it.</summary>
    Task<Result<OAuthTokens>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken cancellationToken);

    /// <summary>A new access token; a new refresh token too when the provider rotates them.</summary>
    Task<Result<OAuthTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes the grant at the provider; <see cref="OAuthRevocation.NotSupported"/> when it has no endpoint for it.</summary>
    Task<OAuthRevocation> RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}

/// <param name="LoginHint">Address proposed to the provider (reconnecting an account).</param>
public sealed record OAuthAuthorization(string State, string CodeChallenge, bool Mail, bool Calendar, string? LoginHint);

/// <param name="RefreshToken">Null when the provider sent none (it keeps the previous one).</param>
/// <param name="Address">E-mail of the account from the ID token (only on a code exchange).</param>
public sealed record OAuthTokens(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt,
    string? Scopes,
    bool HasMail,
    bool HasCalendar,
    string? Address,
    string? DisplayName);

public enum OAuthRevocation
{
    Revoked,
    NotSupported,
    Failed,
}

/// <summary>An IMAP server reached at an address already checked (no second DNS resolution); the host name is used for TLS.</summary>
public sealed record ImapConnectionTarget(string Host, IPAddress Address, int Port, ImapSecurity Security);

public enum ImapTestOutcome
{
    Success,
    AuthenticationFailed,
    ConnectionFailed,
}

/// <summary>Signs in to an IMAP server over TLS and opens the inbox, then disconnects.</summary>
public interface IImapConnectionTester
{
    Task<ImapTestOutcome> TestAsync(ImapConnectionTarget target, string userName, string password, CancellationToken cancellationToken);
}

/// <summary>DNS resolution of a server name entered by a user (every address, so all of them can be checked).</summary>
public interface IHostAddressResolver
{
    /// <returns>Empty when the name does not resolve.</returns>
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken);
}
