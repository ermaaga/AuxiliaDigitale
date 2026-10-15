namespace Auxilia.Contracts.Mailbox;

/// <param name="Provider"><c>google</c>, <c>microsoft</c> or <c>imap</c>.</param>
/// <param name="Color">A colour of the palette (<c>blue</c>, <c>green</c>, <c>orange</c>, <c>purple</c>, <c>red</c>, <c>teal</c>, <c>pink</c>, <c>yellow</c>).</param>
/// <param name="Status"><c>Active</c> or <c>NeedsReconnect</c>.</param>
/// <param name="LastErrorCode"><c>AUX-…</c> code of the last failure when the account needs to be connected again.</param>
public sealed record ConnectedAccountResponse(
    Guid Id,
    string Provider,
    string Address,
    string? DisplayName,
    string Color,
    bool HasMail,
    bool HasCalendar,
    string Status,
    string? LastErrorCode,
    string? ImapHost,
    int? ImapPort,
    DateTimeOffset ConnectedAt);

/// <summary>A provider the user can connect: Google and Microsoft only when their OAuth application is configured.</summary>
/// <param name="Provider"><c>google</c>, <c>microsoft</c> or <c>imap</c>.</param>
public sealed record ConnectedAccountProviderResponse(string Provider, bool SupportsMail, bool SupportsCalendar);

/// <summary>Starts "Sign in with Google / Microsoft": at least one of mail and calendar; <c>accountId</c> to reconnect an account (its address is proposed to the provider and its capabilities kept).</summary>
public sealed record StartOAuthRequest(bool Mail, bool Calendar, Guid? AccountId);

/// <summary>The provider's consent page the browser is sent to.</summary>
public sealed record StartOAuthResponse(Uri AuthorizationUrl);

/// <summary>What the provider sent back to the callback: <c>code</c> and <c>state</c>, or <c>error</c> when access was not granted.</summary>
public sealed record CompleteOAuthRequest(string State, string? Code, string? Error);

/// <param name="Security"><c>SslOnConnect</c> or <c>StartTls</c>; host, port and security are suggested from the address for known providers.</param>
public sealed record ConnectImapRequest(string Address, string UserName, string Password, string Host, int Port, string Security);

/// <summary>An IMAP server known from the domain of the address, or null when the user has to enter it.</summary>
public sealed record ImapServerSuggestionResponse(string? Host, int? Port, string? Security);

public sealed record UpdateConnectedAccountRequest(string Color);
