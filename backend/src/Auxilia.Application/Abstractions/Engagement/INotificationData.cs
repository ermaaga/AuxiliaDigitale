using Auxilia.Domain.Engagement;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Abstractions.Engagement;

/// <summary>Who receives a notification: an active user with the e-mail and language the e-mail channel needs.</summary>
public sealed record NotificationRecipient(Guid UserId, string? Email, string Language);

/// <summary>
/// Notifications and their preferences (F16), one unit of work (inside a write operation it joins the operation's
/// transaction).
/// </summary>
public interface INotificationData : IAsyncDisposable
{
    /// <summary>The user's notifications, newest first.</summary>
    Task<(IReadOnlyList<Notification> Items, int Total)> PageAsync(Guid userId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken);

    Task<int> UnreadCountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The notification of the user (tracked); <c>null</c> when it is not theirs.</summary>
    Task<Notification?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    /// <returns>How many were marked.</returns>
    Task<int> MarkAllReadAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The active users among <paramref name="userIds"/>.</summary>
    Task<IReadOnlyList<NotificationRecipient>> RecipientsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>The active users with the role.</summary>
    Task<IReadOnlyList<NotificationRecipient>> RecipientsWithRoleAsync(TenantRole role, CancellationToken cancellationToken);

    /// <summary>The stored preferences of the users for a kind (missing = defaults).</summary>
    Task<IReadOnlyDictionary<Guid, NotificationPreference>> PreferencesAsync(IReadOnlyCollection<Guid> userIds, string kind, CancellationToken cancellationToken);

    /// <summary>Every stored preference of the user (tracked).</summary>
    Task<IReadOnlyList<NotificationPreference>> PreferencesOfAsync(Guid userId, CancellationToken cancellationToken);

    void Add(Notification notification);

    void Add(NotificationPreference preference);

    void Remove(Notification notification);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface INotificationDataFactory
{
    Task<INotificationData> OpenAsync(CancellationToken cancellationToken);
}
