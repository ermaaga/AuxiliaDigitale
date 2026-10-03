using Auxilia.Application.Abstractions.Engagement;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Engagement;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Engagement;

internal sealed class RequestDataFactory(ITenantDbContextFactory databases) : IRequestDataFactory
{
    public async Task<IRequestData> OpenAsync(CancellationToken cancellationToken) => new RequestData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IRequestData"/>
internal sealed class RequestData(ITenantDbContext db) : IRequestData
{
    public async Task<(IReadOnlyList<RequestRow> Items, int Total)> PageAsync(RequestFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var requests = db.Set<Request>().AsNoTracking();
        var box = filter.Box;
        if (box.SenderUserId is { } sender)
        {
            requests = requests.Where(request => request.SenderUserId == sender);
        }
        else if (box.RecipientUserId is not null || box.Office)
        {
            var recipient = box.RecipientUserId;
            var office = box.Office;
            requests = requests.Where(request => (recipient != null && request.RecipientUserId == recipient) || (office && request.RecipientUserId == null));
        }
        else if (!box.Everything)
        {
            requests = requests.Where(_ => false);
        }

        if (filter.Status is { } status)
        {
            requests = requests.Where(request => request.Status == status);
        }

        if (filter.Type is { } type)
        {
            requests = requests.Where(request => request.Type == type);
        }

        var total = await requests.CountAsync(cancellationToken);
        var sorted = (filter.Sort, filter.Descending) switch
        {
            (RequestSort.LastMessageAt, false) => requests.OrderBy(request => request.LastMessageAt),
            (RequestSort.LastMessageAt, true) => requests.OrderByDescending(request => request.LastMessageAt),
            (RequestSort.Status, false) => requests.OrderBy(request => request.Status).ThenByDescending(request => request.SentAt),
            (RequestSort.Status, true) => requests.OrderByDescending(request => request.Status).ThenByDescending(request => request.SentAt),
            (_, false) => requests.OrderBy(request => request.SentAt),
            (_, true) => requests.OrderByDescending(request => request.SentAt),
        };

        // Names of deleted people stay (single-column lookups with the query filters off).
        var people = db.Set<Person>().IgnoreQueryFilters();
        var users = db.Set<User>();
        var items = await sorted
            .ThenBy(request => request.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .Select(request => new RequestRow(
                request.Id,
                request.Type,
                request.Subject,
                request.Status,
                request.SenderUserId,
                users.Where(user => user.Id == request.SenderUserId)
                    .Join(people, user => user.PersonId, person => person.Id, (user, person) => person.FirstName + " " + person.LastName)
                    .FirstOrDefault() ?? string.Empty,
                request.RecipientUserId,
                users.Where(user => user.Id == request.RecipientUserId)
                    .Join(people, user => user.PersonId, person => person.Id, (user, person) => person.FirstName + " " + person.LastName)
                    .FirstOrDefault(),
                request.SentAt,
                request.LastMessageAt,
                request.Messages.Count))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<Request?> FindAsync(Guid id, bool readOnly, CancellationToken cancellationToken)
    {
        var requests = readOnly ? db.Set<Request>().AsNoTracking() : db.Set<Request>();
        return requests.SingleOrDefaultAsync(request => request.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        userIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await (
                    from user in db.Set<User>()
                    where userIds.Contains(user.Id)
                    join person in db.Set<Person>().IgnoreQueryFilters() on user.PersonId equals person.Id
                    select new { user.Id, Name = person.FirstName + " " + person.LastName })
                .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);

    public Task<Guid?> PersonOfUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<User>().AsNoTracking().Where(user => user.Id == userId).Select(user => (Guid?)user.PersonId).SingleOrDefaultAsync(cancellationToken);

    public void Add(Request request) => db.Set<Request>().Add(request);

    public void Remove(Request request) => db.Set<Request>().Remove(request);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
