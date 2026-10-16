namespace Auxilia.Contracts.Mailbox;

/// <summary>A folder of one of the caller's mailboxes (inbox or sent mail) with its stored message and unread counts.</summary>
/// <param name="Kind"><c>Inbox</c> or <c>Sent</c>.</param>
public sealed record MailFolderResponse(Guid Id, Guid AccountId, string Kind, string Name, int Total, int Unread);

/// <summary>
/// The caller's stored mail headers, newest first: one account and/or folder, words in sender, recipients and subject,
/// unread only.
/// </summary>
public sealed record MailMessageListQuery(Guid? AccountId, Guid? FolderId, string? Search, bool? Unread, int Page, int PageSize);

/// <summary>
/// The header of a message (the body stays at the provider and is read live). Texts are third-party data: shown as
/// text, never as HTML.
/// </summary>
/// <param name="To">Recipients as <c>Name &lt;address&gt;</c> (at most 20).</param>
/// <param name="AttachmentNames">When the provider lists them without the body (IMAP, Gmail); otherwise only <c>hasAttachments</c>.</param>
public sealed record MailMessageResponse(
    Guid Id,
    Guid AccountId,
    Guid FolderId,
    string? FromAddress,
    string? FromName,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    string Subject,
    DateTimeOffset? SentAt,
    DateTimeOffset ReceivedAt,
    bool IsRead,
    long Size,
    bool HasAttachments,
    IReadOnlyList<string> AttachmentNames);
