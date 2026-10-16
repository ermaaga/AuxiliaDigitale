using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Mailbox;

/// <summary>The folders copied from a mailbox (N05, #100): the inbox and the sent mail (v1).</summary>
public enum MailFolderKind
{
    Inbox,
    Sent,
}

/// <summary>A folder read from a provider (Gmail label, Graph well-known folder, IMAP folder).</summary>
public sealed record MailFolderData(string ProviderFolderId, string Name, MailFolderKind Kind);

/// <summary>
/// A folder of a connected mailbox (<c>mailbox.mail_folders</c>, ADR 0021): one per account and kind, so the cursor of
/// the folder (<see cref="SyncCursor.FolderKey"/> = <see cref="CursorKey"/>) survives a renamed folder at the provider.
/// </summary>
public sealed class MailFolder : Entity<Guid>
{
    public const int ProviderFolderIdMaxLength = 500;
    public const int NameMaxLength = 200;

    private MailFolder(Guid id, Guid accountId, MailFolderKind kind)
        : base(id)
    {
        AccountId = accountId;
        Kind = kind;
        ProviderFolderId = string.Empty;
        Name = string.Empty;
    }

    private MailFolder()
    {
        ProviderFolderId = string.Empty;
        Name = string.Empty;
    }

    public Guid AccountId { get; private set; }

    public MailFolderKind Kind { get; private set; }

    public string ProviderFolderId { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The key of the folder's sync cursor (<c>mail:inbox</c>, <c>mail:sent</c>).</summary>
    public string CursorKey => KeyOf(Kind);

    public static string KeyOf(MailFolderKind kind) => $"mail:{kind.ToString().ToLowerInvariant()}";

    public static bool IsUsable(MailFolderData data) =>
        data is not null
        && Enum.IsDefined(data.Kind)
        && !string.IsNullOrWhiteSpace(data.ProviderFolderId)
        && data.ProviderFolderId.Length <= ProviderFolderIdMaxLength;

    public static MailFolder Create(Guid id, Guid accountId, MailFolderData data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!IsUsable(data))
        {
            throw new ArgumentException("The folder is not usable.", nameof(data));
        }

        var folder = new MailFolder(id, accountId, data.Kind);
        folder.Apply(data, now);
        return folder;
    }

    /// <summary>The provider's id and name of the folder (a renamed folder keeps its messages and cursor).</summary>
    public void Apply(MailFolderData data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Kind != Kind || !IsUsable(data))
        {
            throw new ArgumentException("The folder data does not match.", nameof(data));
        }

        ProviderFolderId = data.ProviderFolderId;
        Name = MailText.Clean(data.Name, NameMaxLength) ?? Kind.ToString();
        UpdatedAt = now;
    }
}

/// <summary>A message header read from a provider, before it is stored (adapters fill it, the domain bounds it).</summary>
/// <param name="ProviderMessageId">Unique in the folder: Gmail and Graph message id, IMAP <c>uidvalidity:uid</c>.</param>
/// <param name="To">Recipients as <c>Name &lt;address&gt;</c> (or the address alone).</param>
/// <param name="AttachmentNames">File names when the provider gives them without reading the body (empty otherwise).</param>
public sealed record MailMessageData(
    string ProviderMessageId,
    string? InternetMessageId,
    string? ThreadId,
    string? FromAddress,
    string? FromName,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    string? Subject,
    DateTimeOffset? SentAt,
    DateTimeOffset ReceivedAt,
    bool IsRead,
    long Size,
    bool HasAttachments,
    IReadOnlyList<string> AttachmentNames);

/// <summary>
/// The header of a message of a connected mailbox (<c>mailbox.mail_messages</c>, N05 #100): never the body, every text
/// bounded and cleaned of control characters (third-party data, untrusted). Visible only to the owner of the account;
/// unique per folder and provider id, so a chunk saved twice changes nothing.
/// </summary>
public sealed class MailMessage : Entity<Guid>
{
    public const int ProviderMessageIdMaxLength = 500;
    public const int InternetMessageIdMaxLength = 500;
    public const int ThreadIdMaxLength = 500;
    public const int AddressMaxLength = 320;
    public const int NameMaxLength = 200;
    public const int SubjectMaxLength = 500;
    public const int RecipientMaxLength = 600;
    public const int MaxRecipients = 20;
    public const int AttachmentNameMaxLength = 255;
    public const int MaxAttachmentNames = 20;

    /// <summary>Above this size a header is considered absurd and skipped (N05 "Malformed item").</summary>
    public const long MaxSize = 2L * 1024 * 1024 * 1024;

    private MailMessage(Guid id, Guid accountId, Guid folderId, string providerMessageId)
        : base(id)
    {
        AccountId = accountId;
        FolderId = folderId;
        ProviderMessageId = providerMessageId;
        Subject = string.Empty;
        To = [];
        Cc = [];
        AttachmentNames = [];
    }

    private MailMessage()
    {
        ProviderMessageId = string.Empty;
        Subject = string.Empty;
        To = [];
        Cc = [];
        AttachmentNames = [];
    }

    public Guid AccountId { get; private set; }

    public Guid FolderId { get; private set; }

    public string ProviderMessageId { get; private set; }

    /// <summary>The <c>Message-ID</c> header: recognises a message filed twice (C-06b) across folders and accounts.</summary>
    public string? InternetMessageId { get; private set; }

    public string? ThreadId { get; private set; }

    public string? FromAddress { get; private set; }

    public string? FromName { get; private set; }

    public IReadOnlyList<string> To { get; private set; }

    public IReadOnlyList<string> Cc { get; private set; }

    public string Subject { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>When the mailbox received it (the order of the list).</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    public bool IsRead { get; private set; }

    public long Size { get; private set; }

    public bool HasAttachments { get; private set; }

    public IReadOnlyList<string> AttachmentNames { get; private set; }

    /// <summary>Last chunk that carried the message: a full sync removes the messages it did not see.</summary>
    public DateTimeOffset SyncedAt { get; private set; }

    /// <summary>Whether the header can be stored (otherwise it is skipped and counted).</summary>
    public static bool IsUsable(MailMessageData data) =>
        data is not null
        && !string.IsNullOrWhiteSpace(data.ProviderMessageId)
        && data.ProviderMessageId.Length <= ProviderMessageIdMaxLength
        && data.Size is >= 0 and <= MaxSize
        && data.To is not null && data.Cc is not null && data.AttachmentNames is not null;

    public static MailMessage Create(Guid id, Guid accountId, Guid folderId, MailMessageData data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!IsUsable(data))
        {
            throw new ArgumentException("The message is not usable.", nameof(data));
        }

        var message = new MailMessage(id, accountId, folderId, data.ProviderMessageId);
        message.Apply(data, now);
        return message;
    }

    /// <returns>Whether anything shown changed (read flag, subject…).</returns>
    public bool Apply(MailMessageData data, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        var internetMessageId = MailText.Clean(data.InternetMessageId, InternetMessageIdMaxLength);
        var threadId = MailText.Clean(data.ThreadId, ThreadIdMaxLength);
        var fromAddress = MailText.Clean(data.FromAddress, AddressMaxLength)?.ToLowerInvariant();
        var fromName = MailText.Clean(data.FromName, NameMaxLength);
        var to = MailText.List(data.To, RecipientMaxLength, MaxRecipients);
        var cc = MailText.List(data.Cc, RecipientMaxLength, MaxRecipients);
        var subject = MailText.Clean(data.Subject, SubjectMaxLength) ?? string.Empty;
        var attachments = MailText.List(data.AttachmentNames, AttachmentNameMaxLength, MaxAttachmentNames);
        var hasAttachments = data.HasAttachments || attachments.Length > 0;
        var changed = InternetMessageId != internetMessageId || ThreadId != threadId || FromAddress != fromAddress || FromName != fromName
            || !To.SequenceEqual(to) || !Cc.SequenceEqual(cc) || Subject != subject || SentAt != data.SentAt || ReceivedAt != data.ReceivedAt
            || IsRead != data.IsRead || Size != data.Size || HasAttachments != hasAttachments || !AttachmentNames.SequenceEqual(attachments);
        (InternetMessageId, ThreadId, FromAddress, FromName, To, Cc, Subject) = (internetMessageId, threadId, fromAddress, fromName, to, cc, subject);
        (SentAt, ReceivedAt, IsRead, Size, HasAttachments, AttachmentNames) = (data.SentAt, data.ReceivedAt, data.IsRead, data.Size, hasAttachments, attachments);
        SyncedAt = now;
        return changed;
    }
}

/// <summary>Bounds and cleans third-party texts before they are stored (headers are untrusted data).</summary>
internal static class MailText
{
    public static string? Clean(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        // Control characters (folded header lines, NUL, bidi tricks aside) never reach the page or the log.
        var clean = new string([.. trimmed.Select(character => char.IsControl(character) ? ' ' : character)]).Trim();
        return clean.Length == 0 ? null : clean.Length > maxLength ? clean[..maxLength] : clean;
    }

    public static string[] List(IEnumerable<string>? values, int maxLength, int maxCount) =>
        [.. (values ?? []).Select(value => Clean(value, maxLength)).OfType<string>().Take(maxCount)];
}
