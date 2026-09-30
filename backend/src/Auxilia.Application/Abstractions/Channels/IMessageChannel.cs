using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Abstractions.Channels;

/// <summary>
/// An outbound channel adapter (ARCHITECTURE §6: port here, adapter in <c>Infrastructure/Adapters/Channels/&lt;Provider&gt;</c>),
/// selected by the account's <see cref="Provider"/>. Adding a channel or provider = a new adapter, no change in the
/// calling modules (N03).
/// </summary>
public interface IMessageChannel
{
    /// <summary>Provider key stored on the account (<c>smtp</c>).</summary>
    string Provider { get; }

    MessageChannel Channel { get; }

    /// <summary>Whether the non-secret settings (JSON) are complete and valid for this provider.</summary>
    bool AreSettingsValid(string settingsJson);

    bool IsValidRecipient(string recipient);

    /// <summary>
    /// Sends the message. Throws <see cref="ChannelPermanentException"/> when retrying cannot help (recipient refused,
    /// credentials refused); any other exception is transient and the message is retried.
    /// </summary>
    Task SendAsync(ChannelMessage message, ChannelAccount account, CancellationToken cancellationToken);
}

/// <summary>The account as the adapter needs it: settings JSON and the decrypted secret (used and dropped).</summary>
public sealed record ChannelAccount(string SettingsJson, string? Secret);

public sealed record ChannelMessage(string Recipient, string Subject, string Body);

/// <summary>A delivery failure that retrying cannot fix, with its <c>AUX-</c> code.</summary>
public sealed class ChannelPermanentException : Exception
{
    public ChannelPermanentException(Error error, Exception? innerException = null)
        : base(error?.Description, innerException)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    public ChannelPermanentException()
        : this(Diagnostics.Errors.Host.Unexpected())
    {
    }

    public ChannelPermanentException(string message)
        : base(message)
    {
        Error = Diagnostics.Errors.Host.Unexpected();
    }

    public ChannelPermanentException(string message, Exception innerException)
        : base(message, innerException)
    {
        Error = Diagnostics.Errors.Host.Unexpected();
    }

    public Error Error { get; }
}

/// <summary>Protects account secrets (SMTP passwords, gateway tokens) with Data Protection; decrypted only to send.</summary>
public interface IAccountSecretProtector
{
    string Protect(string secret);

    string Unprotect(string protectedSecret);
}

/// <summary>Liquid templates (Fluid): subject and body of outbound messages.</summary>
public interface ITemplateRenderer
{
    bool IsValid(string template);

    /// <param name="html">Encode the values for HTML (bodies); subjects are plain text.</param>
    /// <returns>The rendered text, or <c>null</c> when the template cannot be parsed or rendered.</returns>
    string? Render(string template, IReadOnlyDictionary<string, object?> model, bool html);
}

/// <summary>
/// Messaging data of the current tenant, one unit of work (one database context; inside a write operation it joins the
/// operation's transaction). Dispose it at the end of the operation.
/// </summary>
public interface IMessagingData : IAsyncDisposable
{
    Task<IReadOnlyList<MessagingAccount>> AccountsAsync(CancellationToken cancellationToken);

    Task<MessagingAccount?> FindAccountAsync(Guid id, CancellationToken cancellationToken);

    void Add(MessagingAccount account);

    /// <param name="channel">Rules of one channel, or every rule when null.</param>
    Task<IReadOnlyList<SenderRule>> RulesAsync(MessageChannel? channel, CancellationToken cancellationToken);

    void Add(SenderRule rule);

    void Remove(SenderRule rule);

    Task<MessageTemplate?> FindTemplateAsync(MessageChannel channel, string code, string language, CancellationToken cancellationToken);

    void Add(OutboundMessage message);

    Task<OutboundMessage?> FindOutboundAsync(Guid id, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IMessagingDataFactory
{
    Task<IMessagingData> OpenAsync(CancellationToken cancellationToken);
}
