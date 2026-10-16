using Auxilia.Domain.Mailbox;

namespace Auxilia.Application.Abstractions.Mailbox;

/// <summary>Sync runs, cursors and copied calendar events of the current tenant (<c>mailbox</c> schema, ADR 0021).</summary>
public interface IMailboxSyncData : IAsyncDisposable
{
    /// <summary>Any user's account (the Worker works on the account of a run); null when it was disconnected.</summary>
    Task<ConnectedAccount?> FindAccountAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConnectedAccount>> AccountsOfAsync(Guid userId, CancellationToken cancellationToken);

    Task<SyncRun?> FindRunAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The newest run of each of the user's accounts for the scope (by queue time).</summary>
    Task<IReadOnlyList<SyncRun>> LatestRunsAsync(Guid userId, SyncScope scope, CancellationToken cancellationToken);

    void Add(SyncRun run);

    Task<SyncCursor?> FindCursorAsync(Guid accountId, SyncScope scope, string folderKey, CancellationToken cancellationToken);

    void Add(SyncCursor cursor);

    /// <summary>The stored events of the account among <paramref name="providerEventIds"/>, by provider event id.</summary>
    Task<IReadOnlyDictionary<string, CalendarEvent>> EventsAsync(Guid accountId, IReadOnlyCollection<string> providerEventIds, CancellationToken cancellationToken);

    void Add(CalendarEvent calendarEvent);

    Task<int> DeleteEventsAsync(Guid accountId, IReadOnlyCollection<string> providerEventIds, CancellationToken cancellationToken);

    /// <summary>After a full sync: the events it did not see (synced before it started), and those ended before the window.</summary>
    Task<int> DeleteStaleEventsAsync(Guid accountId, DateTimeOffset notSyncedSince, DateTimeOffset endedBefore, CancellationToken cancellationToken);

    /// <summary>The events of the user's own accounts overlapping [<paramref name="from"/>, <paramref name="to"/>), by start.</summary>
    Task<IReadOnlyList<CalendarEvent>> EventsOfUserAsync(Guid userId, DateTimeOffset from, DateTimeOffset to, int maxCount, CancellationToken cancellationToken);

    Task<IReadOnlyList<MailFolder>> FoldersAsync(Guid accountId, CancellationToken cancellationToken);

    void Add(MailFolder folder);

    /// <summary>The stored messages of the folder among <paramref name="providerMessageIds"/>, by provider message id.</summary>
    Task<IReadOnlyDictionary<string, MailMessage>> MessagesAsync(Guid folderId, IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken);

    void Add(MailMessage message);

    Task<int> DeleteMessagesAsync(Guid folderId, IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken);

    /// <summary>The folder holds exactly <paramref name="present"/> at the provider: the other stored messages are gone.</summary>
    Task<int> DeleteMessagesNotInAsync(Guid folderId, IReadOnlyCollection<string> present, CancellationToken cancellationToken);

    /// <summary>After a full sync of the folder: the messages it did not see (synced before it started).</summary>
    Task<int> DeleteStaleMessagesAsync(Guid folderId, DateTimeOffset notSyncedSince, CancellationToken cancellationToken);

    /// <summary>The folders of the user's own accounts with mail, with their message and unread counts.</summary>
    Task<IReadOnlyList<MailFolderSummary>> FoldersOfUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>A page of the messages of the user's own accounts, newest first.</summary>
    Task<(IReadOnlyList<MailMessage> Items, long Total)> MessagesOfUserAsync(Guid userId, MailMessageFilter filter, int skip, int take, CancellationToken cancellationToken);

    /// <summary>A message of one of the user's own accounts with its folder and account; null otherwise.</summary>
    Task<OwnedMailMessage?> FindMessageOfUserAsync(Guid userId, Guid messageId, CancellationToken cancellationToken);

    /// <summary>The filings of a message (by the hash of its key), oldest first.</summary>
    Task<IReadOnlyList<FiledMessage>> FilingsAsync(string messageKeyHash, CancellationToken cancellationToken);

    void Add(FiledMessage filed);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IMailboxSyncDataFactory
{
    Task<IMailboxSyncData> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>How a provider call failed, which decides what the sync does (N05 "Errors").</summary>
public enum ProviderFailure
{
    /// <summary>Grant revoked or password changed: no retry, the account needs to be reconnected.</summary>
    Credentials,

    /// <summary>Timeout, network, 429/5xx after the HTTP retries: the bus retries the message later.</summary>
    Transient,

    /// <summary>The sync token is no longer valid (Google 410, Graph 410, UIDVALIDITY changed): bounded full sync.</summary>
    CursorExpired,
}

/// <summary>A classified failure of a mail or calendar provider (adapters throw it; nothing else is expected).</summary>
public sealed class MailboxProviderException : Exception
{
    public MailboxProviderException(ProviderFailure failure, string reason, Exception? innerException = null)
        : base($"{failure}: {reason}", innerException)
    {
        Failure = failure;
        Reason = reason;
    }

    public MailboxProviderException()
        : this(ProviderFailure.Transient, "unknown")
    {
    }

    public MailboxProviderException(string message)
        : this(ProviderFailure.Transient, message)
    {
    }

    public MailboxProviderException(string message, Exception innerException)
        : this(ProviderFailure.Transient, message, innerException)
    {
    }

    public ProviderFailure Failure { get; }

    /// <summary>A short technical reason for the log (HTTP status, provider error code): never data of the account.</summary>
    public string Reason { get; }
}

/// <summary>
/// One page of a calendar read: from <paramref name="PageToken"/> when a sync is in progress, otherwise from
/// <paramref name="SyncToken"/> (incremental) or the window (full sync, <paramref name="SyncToken"/> null).
/// </summary>
public sealed record CalendarPageRequest(
    string AccessToken, string? PageToken, string? SyncToken, DateTimeOffset WindowStart, DateTimeOffset WindowEnd, int PageSize);

/// <param name="Events">New or changed events (malformed items are left out and counted in <paramref name="Malformed"/>).</param>
/// <param name="RemovedIds">Events cancelled or deleted at the provider.</param>
/// <param name="NextPageToken">More pages follow; null on the last page.</param>
/// <param name="SyncToken">On the last page: the token of the next incremental sync.</param>
public sealed record CalendarPage(
    IReadOnlyList<CalendarEventData> Events, IReadOnlyList<string> RemovedIds, string? NextPageToken, string? SyncToken, int Malformed);

/// <summary>The primary calendar of a Google or Microsoft account (read only, ADR 0021); throws <see cref="MailboxProviderException"/>.</summary>
public interface ICalendarProvider
{
    ConnectedAccountProvider Provider { get; }

    Task<CalendarPage> ReadAsync(CalendarPageRequest request, CancellationToken cancellationToken);
}

/// <summary>A stored message with the folder and the account it belongs to.</summary>
public sealed record OwnedMailMessage(MailMessage Message, MailFolder Folder, ConnectedAccount Account);

/// <summary>A folder with its counts (the folder list of the mail page).</summary>
public sealed record MailFolderSummary(MailFolder Folder, int Total, int Unread);

/// <param name="Search">Words looked up in sender, recipients and subject (already trimmed and bounded).</param>
public sealed record MailMessageFilter(Guid? AccountId, Guid? FolderId, string? Search, bool UnreadOnly);

/// <summary>A PEC / IMAP sign-in: the server at an address already checked against SSRF, the user name and the password.</summary>
public sealed record ImapLogin(ImapConnectionTarget Target, string UserName, string Password)
{
    /// <summary>Never the password (nor the user name) in a log or an exception message.</summary>
    public override string ToString() => $"ImapLogin({Target.Host}:{Target.Port})";
}

/// <summary>
/// The access of a sync part to the provider: the OAuth access token (Google, Microsoft) or the IMAP sign-in. Never
/// logged or stored.
/// </summary>
public sealed record MailboxAccess(string? AccessToken, ImapLogin? Imap)
{
    public static MailboxAccess OAuth(string accessToken) => new(accessToken, null);

    public static MailboxAccess Password(ImapLogin login) => new(null, login);

    public override string ToString() => Imap is null ? "MailboxAccess(OAuth)" : "MailboxAccess(IMAP)";
}

/// <summary>
/// One page of the headers of a folder: from <paramref name="PageToken"/> when a sync is in progress, otherwise from
/// <paramref name="SyncToken"/> (incremental) or, for a full sync, the newest messages received since
/// <paramref name="Since"/>, at most <paramref name="MaxMessages"/> in total over the pages (providers that cannot
/// order newest first bound it by days only).
/// </summary>
public sealed record MailPageRequest(
    MailboxAccess Access, MailFolderData Folder, string? PageToken, string? SyncToken, DateTimeOffset Since, int MaxMessages, int PageSize);

/// <param name="Messages">New or changed headers (malformed items are left out and counted in <paramref name="Malformed"/>).</param>
/// <param name="RemovedIds">Messages deleted at the provider or moved out of the folder.</param>
/// <param name="Present">When not null (last page of an IMAP incremental sync): every message id the folder holds now.</param>
/// <param name="NextPageToken">More pages follow; null on the last page.</param>
/// <param name="SyncToken">On the last page: the token of the next incremental sync.</param>
public sealed record MailPage(
    IReadOnlyList<MailMessageData> Messages,
    IReadOnlyList<string> RemovedIds,
    IReadOnlyCollection<string>? Present,
    string? NextPageToken,
    string? SyncToken,
    int Malformed);

/// <summary>
/// The headers of a mailbox (ADR 0021): Gmail and Microsoft Graph over HTTP, PEC / IMAP with MailKit. Read only, never
/// the bodies; throws <see cref="MailboxProviderException"/>.
/// </summary>
public interface IMailProvider
{
    ConnectedAccountProvider Provider { get; }

    /// <summary>The inbox and, when the provider has one, the sent mail.</summary>
    Task<IReadOnlyList<MailFolderData>> FoldersAsync(MailboxAccess access, CancellationToken cancellationToken);

    Task<MailPage> ReadAsync(MailPageRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The original message (RFC 822, a PEC keeps receipt and signature) as a seekable temporary stream deleted when
    /// closed, read live from the provider and cut at <see cref="MailRaw.MaxBytes"/> + 1 bytes; null when the message
    /// is gone (deleted at the provider, or the IMAP UIDs changed).
    /// </summary>
    Task<Stream?> OpenRawAsync(MailboxAccess access, MailFolderData folder, string providerMessageId, CancellationToken cancellationToken);
}

/// <summary>Bounds of a message read live (C-06b).</summary>
public static class MailRaw
{
    /// <summary>Largest original message read (larger ones are refused, <c>AUX-30031</c>).</summary>
    public const long MaxBytes = 50L * 1024 * 1024;

    /// <summary>Characters of the text and of the HTML body returned to the page (the rest is cut, <c>truncated</c>).</summary>
    public const int MaxBodyChars = 1_000_000;
}

/// <summary>An attachment of a message: its position among the attachments is its id.</summary>
public sealed record MailAttachmentInfo(int Index, string FileName, string ContentType, long Size);

/// <summary>The bodies and attachments of an original message (third-party content, untrusted: never rendered as is).</summary>
public sealed record MailContent(string? Text, string? Html, bool Truncated, IReadOnlyList<MailAttachmentInfo> Attachments);

/// <summary>An attachment taken out of a message: a temporary stream deleted when closed.</summary>
public sealed record MailAttachmentContent(Stream Content, MailAttachmentInfo Info);

/// <summary>Reads an original message (MIME, MimeKit in the adapter); malformed parts are tolerated.</summary>
public interface IMailContentReader
{
    Task<MailContent> ReadAsync(Stream raw, int maxBodyChars, CancellationToken cancellationToken);

    /// <summary>The decoded attachment at <paramref name="index"/>; null when there is none.</summary>
    Task<MailAttachmentContent?> OpenAttachmentAsync(Stream raw, int index, CancellationToken cancellationToken);
}
