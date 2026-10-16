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

/// <summary>
/// A message read live from the provider (C-06b): its stored header, the bodies (third-party content: the text is shown as
/// text, the HTML only in a sandboxed frame without scripts nor remote images), the attachments and the cases (visible to
/// the caller) it was already filed into.
/// </summary>
/// <param name="Truncated">A body was longer than the page shows (the <c>.eml</c> has it all).</param>
public sealed record MailMessageDetailResponse(
    MailMessageResponse Message,
    string? Text,
    string? Html,
    bool Truncated,
    IReadOnlyList<MailAttachmentResponse> Attachments,
    IReadOnlyList<FiledCaseResponse> FiledIn);

/// <summary>An attachment of a message; <c>index</c> is its id in the download and filing calls.</summary>
public sealed record MailAttachmentResponse(int Index, string FileName, string ContentType, long Size);

/// <summary>A case the message was filed into.</summary>
public sealed record FiledCaseResponse(Guid CaseId, string CaseNumber, DateTimeOffset FiledAt);

/// <summary>
/// File a message into a case (F10 rules of the case): the original <c>.eml</c> and, optionally, some attachments as
/// separate documents, in the folder of the case (optional). <c>force</c>: file it again into a case that has it already.
/// </summary>
public sealed record FileMailRequest(Guid CaseId, Guid? FolderId, IReadOnlyList<int>? Attachments, string? Description, bool Force);

/// <summary>The documents created: the <c>.eml</c> and the attachments, in the order asked.</summary>
public sealed record FileMailResponse(Guid MessageDocumentId, IReadOnlyList<Guid> AttachmentDocumentIds);
