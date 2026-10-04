namespace Auxilia.Contracts.Engagement;

/// <summary>
/// A new request (F15): <c>type</c> <c>Information</c>, <c>General</c>, <c>Support</c> or <c>Appointment</c> (Q18),
/// subject (≤ 200) and message (≤ 4000). Clients ask the employee in charge when <c>askMyOperator</c> (default) and
/// they have one, otherwise the office; employees always ask the office.
/// </summary>
public sealed record CreateRequestRequest(string Type, string Subject, string Message, bool? AskMyOperator);

/// <summary>A message appended to the thread (≤ 4000 characters).</summary>
public sealed record ReplyToRequestRequest(string Message);

/// <summary>A participant of a request (name empty when unknown).</summary>
public sealed record RequestUserResponse(Guid UserId, string FullName);

/// <summary>
/// A request in the inbox: <c>status</c> <c>Pending</c>, <c>Responded</c> or <c>Closed</c>; <c>recipient</c> null
/// for the office (the Administrators).
/// </summary>
public sealed record RequestListItemResponse(
    Guid Id,
    string Type,
    string Subject,
    string Status,
    RequestUserResponse Sender,
    RequestUserResponse? Recipient,
    DateTimeOffset SentAt,
    DateTimeOffset LastMessageAt,
    int MessageCount);

/// <summary>A message of the thread; <c>mine</c> when the caller wrote it.</summary>
public sealed record RequestMessageResponse(Guid Id, RequestUserResponse Author, string Body, DateTimeOffset SentAt, bool Mine);

/// <summary>A request with its thread and what the caller may do.</summary>
public sealed record RequestResponse(
    Guid Id,
    string Type,
    string Subject,
    string Status,
    RequestUserResponse Sender,
    RequestUserResponse? Recipient,
    DateTimeOffset SentAt,
    DateTimeOffset LastMessageAt,
    DateTimeOffset? ClosedAt,
    IReadOnlyList<RequestMessageResponse> Messages,
    bool CanReply,
    bool CanClose,
    bool CanDelete);

/// <summary>
/// The inbox (F15, Q16): <c>box</c> <c>received</c> (employees: addressed to them; Administrators: to the office),
/// <c>sent</c> (by the caller) or <c>all</c> (Administrators only); default <c>sent</c> for clients, <c>received</c>
/// otherwise. <c>sort</c> one of <c>sentAt</c> (default, descending), <c>lastMessageAt</c>, <c>status</c>; <c>-</c>
/// for descending.
/// </summary>
public sealed record RequestListQuery(string? Box, string? Status, string? Type, string? Sort, int Page, int PageSize);
