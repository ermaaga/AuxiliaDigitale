using System.Text.Json;

namespace Auxilia.Contracts.Engagement;

/// <summary>
/// A notification (F16): the web app renders <c>notifications.{kind}.title</c> and <c>.message</c> with
/// <c>parameters</c> in the reader's language; <c>link</c> is the tenant route to open (null when the record is gone).
/// </summary>
public sealed record NotificationResponse(Guid Id, string Kind, JsonElement Parameters, string? Link, Guid? EntityId, DateTimeOffset CreatedAt, bool IsRead);

public sealed record UnreadNotificationsResponse(int Count);

/// <summary>How the caller wants one kind: in the app and by e-mail (defaults: in-app on, e-mail off).</summary>
public sealed record NotificationPreferenceResponse(string Kind, bool InApp, bool Email);

public sealed record NotificationPreferenceItem(string Kind, bool InApp, bool Email);

/// <summary>Preferences to change (kinds not listed keep theirs).</summary>
public sealed record SetNotificationPreferencesRequest(IReadOnlyList<NotificationPreferenceItem> Items);

/// <summary>The caller's notifications, newest first; <c>unreadOnly</c> keeps the unread ones.</summary>
public sealed record NotificationListQuery(bool UnreadOnly, int Page, int PageSize);
