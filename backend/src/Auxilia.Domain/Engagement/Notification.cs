using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Engagement;

/// <summary>
/// An in-app notification of a user (<c>engagement.notifications</c>, legacy <c>Notification</c>, F16). No text is
/// stored: <see cref="Kind"/> (e.g. <c>appointment.approved</c>) and <see cref="Parameters"/> are rendered in the
/// reader's language (keys <c>notifications.{kind}.title|message</c>); <see cref="Link"/> is the page to open (Q12:
/// per type and role). The user reads and deletes their own.
/// </summary>
public sealed class Notification : AggregateRoot<Guid>
{
    public const int KindMaxLength = 60;
    public const int LinkMaxLength = 300;

    public Notification(Guid id, Guid userId, string kind, string parameters, string? link, Guid? entityId, DateTimeOffset createdAt)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameters);
        UserId = userId;
        Kind = kind;
        Parameters = parameters;
        Link = link;
        EntityId = entityId;
        CreatedAt = createdAt;
    }

    private Notification()
    {
        Kind = Parameters = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string Kind { get; private set; }

    /// <summary>Values of the text placeholders (JSON object of strings).</summary>
    public string Parameters { get; private set; }

    /// <summary>The tenant route to open (without the tenant slug), e.g. <c>/appointments?open={id}</c>.</summary>
    public string? Link { get; private set; }

    /// <summary>The record it is about (legacy <c>RelatedEntityId</c>).</summary>
    public Guid? EntityId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    /// <returns>Whether it changed (already read stays as it was).</returns>
    public bool MarkRead(DateTimeOffset now)
    {
        if (ReadAt is not null)
        {
            return false;
        }

        ReadAt = now;
        return true;
    }
}

/// <summary>
/// How a user wants one kind of notification (<c>engagement.notification_preferences</c>, F16): in the app (default
/// on) and by e-mail (default off). Without a row the defaults apply.
/// </summary>
public sealed class NotificationPreference
{
    public NotificationPreference(Guid userId, string kind, bool inApp, bool email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        UserId = userId;
        Kind = kind;
        InApp = inApp;
        Email = email;
    }

    private NotificationPreference()
    {
        Kind = string.Empty;
    }

    public Guid UserId { get; private set; }

    public string Kind { get; private set; }

    public bool InApp { get; private set; }

    public bool Email { get; private set; }

    public void Set(bool inApp, bool email)
    {
        InApp = inApp;
        Email = email;
    }
}
