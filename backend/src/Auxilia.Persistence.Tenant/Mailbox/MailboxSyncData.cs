using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Mailbox;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Mailbox;

internal sealed class MailboxSyncDataFactory(ITenantDbContextFactory databases) : IMailboxSyncDataFactory
{
    public async Task<IMailboxSyncData> OpenAsync(CancellationToken cancellationToken) => new MailboxSyncData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IMailboxSyncData"/>
internal sealed class MailboxSyncData(ITenantDbContext db) : IMailboxSyncData
{
    public Task<ConnectedAccount?> FindAccountAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<ConnectedAccount>().SingleOrDefaultAsync(account => account.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ConnectedAccount>> AccountsOfAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<ConnectedAccount>()
            .Where(account => account.UserId == userId)
            .OrderBy(account => account.ConnectedAt)
            .ThenBy(account => account.Id)
            .ToListAsync(cancellationToken);

    public Task<SyncRun?> FindRunAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<SyncRun>().SingleOrDefaultAsync(run => run.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SyncRun>> LatestRunsAsync(Guid userId, SyncScope scope, CancellationToken cancellationToken)
    {
        var accounts = db.Set<ConnectedAccount>().Where(account => account.UserId == userId).Select(account => account.Id);
        return await db.Set<SyncRun>()
            .Where(run => run.Scope == scope && accounts.Contains(run.AccountId))
            .GroupBy(run => run.AccountId)
            .Select(runs => runs.OrderByDescending(run => run.QueuedAt).ThenByDescending(run => run.Id).First())
            .ToListAsync(cancellationToken);
    }

    public void Add(SyncRun run) => db.Set<SyncRun>().Add(run);

    public Task<SyncCursor?> FindCursorAsync(Guid accountId, SyncScope scope, string folderKey, CancellationToken cancellationToken) =>
        db.Set<SyncCursor>().SingleOrDefaultAsync(
            cursor => cursor.AccountId == accountId && cursor.Scope == scope && cursor.FolderKey == folderKey, cancellationToken);

    public void Add(SyncCursor cursor) => db.Set<SyncCursor>().Add(cursor);

    public async Task<IReadOnlyDictionary<string, CalendarEvent>> EventsAsync(
        Guid accountId, IReadOnlyCollection<string> providerEventIds, CancellationToken cancellationToken)
    {
        if (providerEventIds.Count == 0)
        {
            return new Dictionary<string, CalendarEvent>(StringComparer.Ordinal);
        }

        return await db.Set<CalendarEvent>()
            .Where(item => item.AccountId == accountId && providerEventIds.Contains(item.ProviderEventId))
            .ToDictionaryAsync(item => item.ProviderEventId, StringComparer.Ordinal, cancellationToken);
    }

    public void Add(CalendarEvent calendarEvent) => db.Set<CalendarEvent>().Add(calendarEvent);

    public Task<int> DeleteEventsAsync(Guid accountId, IReadOnlyCollection<string> providerEventIds, CancellationToken cancellationToken) =>
        providerEventIds.Count == 0
            ? Task.FromResult(0)
            : db.Set<CalendarEvent>()
                .Where(item => item.AccountId == accountId && providerEventIds.Contains(item.ProviderEventId))
                .ExecuteDeleteAsync(cancellationToken);

    public Task<int> DeleteStaleEventsAsync(Guid accountId, DateTimeOffset notSyncedSince, DateTimeOffset endedBefore, CancellationToken cancellationToken) =>
        db.Set<CalendarEvent>()
            .Where(item => item.AccountId == accountId && (item.SyncedAt < notSyncedSince || item.EndsAt <= endedBefore))
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<CalendarEvent>> EventsOfUserAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, int maxCount, CancellationToken cancellationToken)
    {
        var accounts = db.Set<ConnectedAccount>().Where(account => account.UserId == userId && account.HasCalendar).Select(account => account.Id);
        return await db.Set<CalendarEvent>()
            .AsNoTracking()
            .Where(item => accounts.Contains(item.AccountId) && item.StartsAt < to && item.EndsAt > from)
            .OrderBy(item => item.StartsAt)
            .ThenBy(item => item.Id)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
