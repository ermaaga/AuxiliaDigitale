using System.Text.RegularExpressions;

using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Mailbox;

/// <summary>Where a connected account lives (N05): Google and Microsoft through OAuth, any other mailbox (PEC) through IMAP.</summary>
public enum ConnectedAccountProvider
{
    Google,
    Microsoft,
    Imap,
}

/// <summary><c>NeedsReconnect</c>: the provider refused the stored grant or password; the owner connects it again.</summary>
public enum ConnectedAccountStatus
{
    Active,
    NeedsReconnect,
}

/// <summary>How the IMAP connection is secured: never in clear text (N05).</summary>
public enum ImapSecurity
{
    SslOnConnect,
    StartTls,
}

/// <summary>An IMAP server: host name as entered (or suggested) and port.</summary>
public sealed record ImapServer(string Host, int Port, ImapSecurity Security);

/// <summary>
/// An external mailbox and/or calendar of one user (<c>mailbox.connected_accounts</c>, N05, ADR 0021): only its owner sees
/// it. The secret (OAuth refresh token or IMAP password) is stored protected by the application layer; this aggregate
/// never sees it in clear. One account per user, provider and address: connecting it again replaces grant and secret.
/// </summary>
public sealed partial class ConnectedAccount : AggregateRoot<Guid>
{
    public const int AddressMaxLength = 320;
    public const int DisplayNameMaxLength = 200;
    public const int ScopesMaxLength = 2000;
    public const int HostMaxLength = 253;
    public const int UserNameMaxLength = 320;
    public const int ErrorCodeMaxLength = 20;

    /// <summary>Colours of the theme palette (the web app maps them to its tokens).</summary>
    public static readonly IReadOnlyList<string> Colors = ["blue", "green", "orange", "purple", "red", "teal", "pink", "yellow"];

    private ConnectedAccount(Guid id, Guid userId, ConnectedAccountProvider provider, string address, DateTimeOffset now)
        : base(id)
    {
        UserId = userId;
        Provider = provider;
        Address = address;
        Color = Colors[0];
        ProtectedSecret = string.Empty;
        Status = ConnectedAccountStatus.Active;
        ConnectedAt = now;
        UpdatedAt = now;
    }

    private ConnectedAccount()
    {
        Address = string.Empty;
        Color = Colors[0];
        ProtectedSecret = string.Empty;
    }

    public Guid UserId { get; private set; }

    public ConnectedAccountProvider Provider { get; private set; }

    /// <summary>The e-mail address of the account, lower case.</summary>
    public string Address { get; private set; }

    public string? DisplayName { get; private set; }

    public string Color { get; private set; }

    public bool HasMail { get; private set; }

    public bool HasCalendar { get; private set; }

    /// <summary>OAuth scopes granted by the provider (space separated); null for IMAP.</summary>
    public string? GrantedScopes { get; private set; }

    public string? ImapHost { get; private set; }

    public int? ImapPort { get; private set; }

    public ImapSecurity? ImapSecurity { get; private set; }

    public string? ImapUserName { get; private set; }

    /// <summary>Refresh token or IMAP password, protected per tenant before it reaches the domain.</summary>
    public string ProtectedSecret { get; private set; }

    public ConnectedAccountStatus Status { get; private set; }

    /// <summary><c>AUX-…</c> code of the last failure (e.g. the grant was revoked), null when the account works.</summary>
    public string? LastErrorCode { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static string NormalizeAddress(string? address) => address?.Trim().ToLowerInvariant() ?? string.Empty;

    public static bool IsValidAddress(string address) =>
        address.Length is > 0 and <= AddressMaxLength && AddressPattern().IsMatch(address);

    /// <summary>Checks address, server and user name of a PEC / IMAP account before anything is contacted.</summary>
    public static Result ValidateImap(string? address, ImapServer server, string? userName)
    {
        ArgumentNullException.ThrowIfNull(server);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        CheckAddress(errors, NormalizeAddress(address));
        CheckImap(errors, server, userName);
        return errors.Count > 0 ? Errors.Mailbox.AccountInvalid(errors) : Result.Success();
    }

    /// <summary>A Google or Microsoft account after the provider's consent.</summary>
    public static Result<ConnectedAccount> ConnectOAuth(
        Guid id, Guid userId, ConnectedAccountProvider provider, OAuthGrantDetails grant, string color, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (provider == ConnectedAccountProvider.Imap)
        {
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "IMAP accounts are connected with a password.");
        }

        var address = NormalizeAddress(grant.Address);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        CheckAddress(errors, address);
        CheckColor(errors, color);
        if (errors.Count > 0)
        {
            return Errors.Mailbox.AccountInvalid(errors);
        }

        var account = new ConnectedAccount(id, userId, provider, address, now) { Color = color };
        account.ApplyGrant(grant, now);
        return account;
    }

    /// <summary>A PEC / IMAP account after a successful connection test (mail only).</summary>
    public static Result<ConnectedAccount> ConnectImap(
        Guid id, Guid userId, string address, ImapServer server, string userName, string protectedPassword, string color, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(server);

        var normalized = NormalizeAddress(address);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        CheckAddress(errors, normalized);
        CheckColor(errors, color);
        CheckImap(errors, server, userName);
        if (errors.Count > 0)
        {
            return Errors.Mailbox.AccountInvalid(errors);
        }

        var account = new ConnectedAccount(id, userId, ConnectedAccountProvider.Imap, normalized, now) { Color = color, HasMail = true };
        account.ApplyImap(server, userName.Trim(), protectedPassword, now);
        return account;
    }

    /// <summary>
    /// The owner connected the same Google / Microsoft account again: new secret (when the provider sent one) and the
    /// capabilities granted now added to the previous ones (incremental consent); the account works again.
    /// </summary>
    public void Reconnect(OAuthGrantDetails grant, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (Provider == ConnectedAccountProvider.Imap)
        {
            throw new InvalidOperationException("IMAP accounts are reconnected with a password.");
        }

        ApplyGrant(grant with { HasMail = grant.HasMail || HasMail, HasCalendar = grant.HasCalendar || HasCalendar }, now);
    }

    /// <summary>The owner connected the same PEC / IMAP account again (new password or server).</summary>
    public Result ReconnectImap(ImapServer server, string userName, string protectedPassword, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (Provider != ConnectedAccountProvider.Imap)
        {
            throw new InvalidOperationException("Only IMAP accounts have a password.");
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        CheckImap(errors, server, userName);
        if (errors.Count > 0)
        {
            return Errors.Mailbox.AccountInvalid(errors);
        }

        ApplyImap(server, userName.Trim(), protectedPassword, now);
        return Result.Success();
    }

    public Result ChangeColor(string color, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        CheckColor(errors, color);
        if (errors.Count > 0)
        {
            return Errors.Mailbox.AccountInvalid(errors);
        }

        Color = color;
        UpdatedAt = now;
        return Result.Success();
    }

    /// <summary>The provider sent a new refresh token (Microsoft rotates them): it replaces the stored one.</summary>
    public void RotateSecret(string protectedSecret, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedSecret);
        ProtectedSecret = protectedSecret;
        UpdatedAt = now;
    }

    /// <summary>The provider refused the grant or the password: the owner has to connect the account again.</summary>
    public void MarkNeedsReconnect(string errorCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(errorCode);
        Status = ConnectedAccountStatus.NeedsReconnect;
        LastErrorCode = errorCode.Length > ErrorCodeMaxLength ? errorCode[..ErrorCodeMaxLength] : errorCode;
        UpdatedAt = now;
    }

    /// <summary>The first colour of the palette not used by the other accounts of the user (the first one when all are).</summary>
    public static string NextColor(IEnumerable<string> used)
    {
        var taken = used.ToHashSet(StringComparer.Ordinal);
        return Colors.FirstOrDefault(color => !taken.Contains(color)) ?? Colors[0];
    }

    private void ApplyGrant(OAuthGrantDetails grant, DateTimeOffset now)
    {
        DisplayName = Bounded(grant.DisplayName, DisplayNameMaxLength);
        HasMail = grant.HasMail;
        HasCalendar = grant.HasCalendar;
        GrantedScopes = Bounded(grant.Scopes, ScopesMaxLength);
        if (!string.IsNullOrEmpty(grant.ProtectedRefreshToken))
        {
            ProtectedSecret = grant.ProtectedRefreshToken;
        }

        Status = ConnectedAccountStatus.Active;
        LastErrorCode = null;
        UpdatedAt = now;
    }

    private void ApplyImap(ImapServer server, string userName, string protectedPassword, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedPassword);
        ImapHost = server.Host.Trim().ToLowerInvariant();
        ImapPort = server.Port;
        ImapSecurity = server.Security;
        ImapUserName = userName;
        ProtectedSecret = protectedPassword;
        Status = ConnectedAccountStatus.Active;
        LastErrorCode = null;
        UpdatedAt = now;
    }

    private static void CheckAddress(Dictionary<string, string[]> errors, string address)
    {
        if (!IsValidAddress(address))
        {
            errors["address"] = ["validation.mailbox.address"];
        }
    }

    private static void CheckColor(Dictionary<string, string[]> errors, string color)
    {
        if (!Colors.Contains(color))
        {
            errors["color"] = ["validation.mailbox.color"];
        }
    }

    private static void CheckImap(Dictionary<string, string[]> errors, ImapServer server, string? userName)
    {
        var host = server.Host?.Trim() ?? string.Empty;
        if (host.Length is 0 or > HostMaxLength || !HostPattern().IsMatch(host))
        {
            errors["host"] = ["validation.mailbox.host"];
        }

        if (server.Port is < 1 or > 65535 || !Enum.IsDefined(server.Security))
        {
            errors["port"] = ["validation.mailbox.port"];
        }

        var name = userName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > UserNameMaxLength)
        {
            errors["userName"] = ["validation.mailbox.userName"];
        }
    }

    private static string? Bounded(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex AddressPattern();

    /// <summary>A DNS name (labels of letters, digits and hyphens); IP literals are refused (the address is checked after resolution).</summary>
    [GeneratedRegex(@"^(?=.*[a-zA-Z])[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+$", RegexOptions.CultureInvariant)]
    private static partial Regex HostPattern();
}

/// <summary>What the provider granted: identity of the account, capabilities, scopes and the protected refresh token (null when the provider sent none).</summary>
public sealed record OAuthGrantDetails(string Address, string? DisplayName, bool HasMail, bool HasCalendar, string? Scopes, string? ProtectedRefreshToken);
