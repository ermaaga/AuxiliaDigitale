using Auxilia.Application.Abstractions.Engagement;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Engagement;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Engagement;

internal sealed class NotificationDataFactory(ITenantDbContextFactory databases) : INotificationDataFactory
{
    public async Task<INotificationData> OpenAsync(CancellationToken cancellationToken) => new NotificationData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="INotificationData"/>
internal sealed class NotificationData(ITenantDbContext db) : INotificationData
{
    public async Task<(IReadOnlyList<Notification> Items, int Total)> PageAsync(Guid userId, bool unreadOnly, int skip, int take, CancellationToken cancellationToken)
    {
        var notifications = db.Set<Notification>().AsNoTracking().Where(notification => notification.UserId == userId);
        if (unreadOnly)
        {
            notifications = notifications.Where(notification => notification.ReadAt == null);
        }

        var total = await notifications.CountAsync(cancellationToken);
        var items = await notifications
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<int> UnreadCountAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<Notification>().CountAsync(notification => notification.UserId == userId && notification.ReadAt == null, cancellationToken);

    public Task<Notification?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        db.Set<Notification>().SingleOrDefaultAsync(notification => notification.Id == id && notification.UserId == userId, cancellationToken);

    public Task<int> MarkAllReadAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<Notification>()
            .Where(notification => notification.UserId == userId && notification.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(notification => notification.ReadAt, now), cancellationToken);

    public async Task<IReadOnlyList<NotificationRecipient>> RecipientsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        userIds.Count == 0 ? [] : await Recipients(db.Set<User>().Where(user => userIds.Contains(user.Id))).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NotificationRecipient>> RecipientsWithRoleAsync(TenantRole role, CancellationToken cancellationToken) =>
        await Recipients(db.Set<User>().Where(user => EF.Property<List<UserRole>>(user, "roles").Any(item => item.Role == role))).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, NotificationPreference>> PreferencesAsync(
        IReadOnlyCollection<Guid> userIds, string kind, CancellationToken cancellationToken) =>
        await db.Set<NotificationPreference>().AsNoTracking()
            .Where(preference => userIds.Contains(preference.UserId) && preference.Kind == kind)
            .ToDictionaryAsync(preference => preference.UserId, cancellationToken);

    public async Task<IReadOnlyList<NotificationPreference>> PreferencesOfAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<NotificationPreference>().Where(preference => preference.UserId == userId).ToListAsync(cancellationToken);

    public void Add(Notification notification) => db.Set<Notification>().Add(notification);

    public void Add(NotificationPreference preference) => db.Set<NotificationPreference>().Add(preference);

    public void Remove(Notification notification) => db.Set<Notification>().Remove(notification);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    /// <summary>Active users whose person is not deleted (joined, so its soft-delete filter applies).</summary>
    private IQueryable<NotificationRecipient> Recipients(IQueryable<User> users) =>
        from user in users.AsNoTracking()
        where user.IsActive
        join person in db.Set<Person>() on user.PersonId equals person.Id
        select new NotificationRecipient(user.Id, user.Email, user.LanguageCode);
}
