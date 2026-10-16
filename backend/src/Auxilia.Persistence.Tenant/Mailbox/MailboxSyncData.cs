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

    public async Task<IReadOnlyList<MailFolder>> FoldersAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.Set<MailFolder>().Where(folder => folder.AccountId == accountId).OrderBy(folder => folder.Kind).ToListAsync(cancellationToken);

    public void Add(MailFolder folder) => db.Set<MailFolder>().Add(folder);

    public async Task<IReadOnlyDictionary<string, MailMessage>> MessagesAsync(
        Guid folderId, IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken)
    {
        if (providerMessageIds.Count == 0)
        {
            return new Dictionary<string, MailMessage>(StringComparer.Ordinal);
        }

        return await db.Set<MailMessage>()
            .Where(message => message.FolderId == folderId && providerMessageIds.Contains(message.ProviderMessageId))
            .ToDictionaryAsync(message => message.ProviderMessageId, StringComparer.Ordinal, cancellationToken);
    }

    public void Add(MailMessage message) => db.Set<MailMessage>().Add(message);

    public Task<int> DeleteMessagesAsync(Guid folderId, IReadOnlyCollection<string> providerMessageIds, CancellationToken cancellationToken) =>
        providerMessageIds.Count == 0
            ? Task.FromResult(0)
            : db.Set<MailMessage>()
                .Where(message => message.FolderId == folderId && providerMessageIds.Contains(message.ProviderMessageId))
                .ExecuteDeleteAsync(cancellationToken);

    public Task<int> DeleteMessagesNotInAsync(Guid folderId, IReadOnlyCollection<string> present, CancellationToken cancellationToken)
    {
        // The headers added by the same chunk are inserted afterwards by SaveChanges (and are among the present ones anyway).
        var keep = present.ToArray();
        return db.Set<MailMessage>()
            .Where(message => message.FolderId == folderId && !keep.Contains(message.ProviderMessageId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public Task<int> DeleteStaleMessagesAsync(Guid folderId, DateTimeOffset notSyncedSince, CancellationToken cancellationToken) =>
        db.Set<MailMessage>()
            .Where(message => message.FolderId == folderId && message.SyncedAt < notSyncedSince)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<MailFolderSummary>> FoldersOfUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var accounts = db.Set<ConnectedAccount>().Where(account => account.UserId == userId && account.HasMail);
        var rows = await db.Set<MailFolder>()
            .AsNoTracking()
            .Join(accounts, folder => folder.AccountId, account => account.Id, (folder, account) => new { folder, account.ConnectedAt })
            .OrderBy(row => row.ConnectedAt)
            .ThenBy(row => row.folder.AccountId)
            .ThenBy(row => row.folder.Kind)
            .Select(row => new
            {
                row.folder,
                Total = db.Set<MailMessage>().Count(message => message.FolderId == row.folder.Id),
                Unread = db.Set<MailMessage>().Count(message => message.FolderId == row.folder.Id && !message.IsRead),
            })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new MailFolderSummary(row.folder, row.Total, row.Unread))];
    }

    public async Task<(IReadOnlyList<MailMessage> Items, long Total)> MessagesOfUserAsync(
        Guid userId, MailMessageFilter filter, int skip, int take, CancellationToken cancellationToken)
    {
        var accounts = db.Set<ConnectedAccount>().Where(account => account.UserId == userId && account.HasMail).Select(account => account.Id);
        var messages = db.Set<MailMessage>().AsNoTracking().Where(message => accounts.Contains(message.AccountId));
        if (filter.AccountId is { } accountId)
        {
            messages = messages.Where(message => message.AccountId == accountId);
        }

        if (filter.FolderId is { } folderId)
        {
            messages = messages.Where(message => message.FolderId == folderId);
        }

        if (filter.UnreadOnly)
        {
            messages = messages.Where(message => !message.IsRead);
        }

        if (filter.Search is { Length: > 0 } search)
        {
            var pattern = "%" + search.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            messages = messages.Where(message => EF.Functions.ILike(message.Subject, pattern, "\\")
                || (message.FromAddress != null && EF.Functions.ILike(message.FromAddress, pattern, "\\"))
                || (message.FromName != null && EF.Functions.ILike(message.FromName, pattern, "\\"))
                || message.To.Any(recipient => EF.Functions.ILike(recipient, pattern, "\\")));
        }

        var total = await messages.LongCountAsync(cancellationToken);
        var items = await messages
            .OrderByDescending(message => message.ReceivedAt)
            .ThenByDescending(message => message.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<OwnedMailMessage?> FindMessageOfUserAsync(Guid userId, Guid messageId, CancellationToken cancellationToken)
    {
        var row = await (
                from message in db.Set<MailMessage>()
                join folder in db.Set<MailFolder>() on message.FolderId equals folder.Id
                join account in db.Set<ConnectedAccount>() on message.AccountId equals account.Id
                where message.Id == messageId && account.UserId == userId
                select new { message, folder, account })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new OwnedMailMessage(row.message, row.folder, row.account);
    }

    public async Task<IReadOnlyList<FiledMessage>> FilingsAsync(string messageKeyHash, CancellationToken cancellationToken) =>
        await db.Set<FiledMessage>()
            .AsNoTracking()
            .Where(filed => filed.MessageKeyHash == messageKeyHash)
            .OrderBy(filed => filed.FiledAt)
            .ToListAsync(cancellationToken);

    public void Add(FiledMessage filed) => db.Set<FiledMessage>().Add(filed);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
