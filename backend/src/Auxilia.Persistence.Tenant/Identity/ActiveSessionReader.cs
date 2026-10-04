using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Identity;

/// <inheritdoc cref="IActiveSessionReader"/>
internal sealed class ActiveSessionReader(ITenantDbContextFactory databases) : IActiveSessionReader
{
    public async Task<(IReadOnlyList<ActiveSessionRow> Items, int Total)> PageAsync(ActiveSessionFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var db = await databases.CreateAsync(cancellationToken);
        var now = filter.Now;
        var sessions =
            from session in Open(db, now)
            join user in db.Set<User>().AsNoTracking() on session.UserId equals user.Id
            join person in db.Set<Person>().IgnoreQueryFilters() on user.PersonId equals person.Id
            select new { session, user, person };

        if (filter.UserName is { } text)
        {
            var pattern = "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            sessions = sessions.Where(row => EF.Functions.ILike(row.user.UserName, pattern, "\\")
                || EF.Functions.ILike(row.person.FirstName + " " + row.person.LastName, pattern, "\\"));
        }

        var total = await sessions.CountAsync(cancellationToken);
        var sorted = (filter.Sort, filter.Descending) switch
        {
            (ActiveSessionSort.CreatedAt, false) => sessions.OrderBy(row => row.session.CreatedAt),
            (ActiveSessionSort.CreatedAt, true) => sessions.OrderByDescending(row => row.session.CreatedAt),
            (ActiveSessionSort.UserName, false) => sessions.OrderBy(row => row.user.UserName),
            (ActiveSessionSort.UserName, true) => sessions.OrderByDescending(row => row.user.UserName),
            (_, false) => sessions.OrderBy(row => row.session.LastUsedAt),
            (_, true) => sessions.OrderByDescending(row => row.session.LastUsedAt),
        };

        var page = await sorted
            .ThenBy(row => row.session.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .Select(row => new
            {
                row.session.Id,
                row.session.UserId,
                row.user.UserName,
                FullName = row.person.FirstName + " " + row.person.LastName,
                Roles = EF.Property<List<UserRole>>(row.user, "roles").Select(item => item.Role).ToList(),
                row.session.ClientId,
                row.session.IpAddress,
                row.session.UserAgent,
                row.session.CreatedAt,
                row.session.LastUsedAt,
                row.session.IdleExpiresAt,
                row.session.AbsoluteExpiresAt,
            })
            .ToListAsync(cancellationToken);
        return (page.Select(row => new ActiveSessionRow(
                row.Id, row.UserId, row.UserName, row.FullName, row.Roles, row.ClientId, row.IpAddress, row.UserAgent, row.CreatedAt, row.LastUsedAt,
                row.IdleExpiresAt < row.AbsoluteExpiresAt ? row.IdleExpiresAt : row.AbsoluteExpiresAt)).ToArray(),
            total);
    }

    public async Task<ActiveSessionCounts> CountAsync(DateTimeOffset now, DateTimeOffset activeSince, CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        var open = Open(db, now);
        return new ActiveSessionCounts(
            await open.CountAsync(cancellationToken),
            await open.Select(session => session.UserId).Distinct().CountAsync(cancellationToken),
            await open.CountAsync(session => session.LastUsedAt >= activeSince, cancellationToken));
    }

    /// <summary>Not ended and before both expiries (<see cref="RefreshSession.IsActiveAt"/>).</summary>
    private static IQueryable<RefreshSession> Open(ITenantDbContext db, DateTimeOffset now) =>
        db.Set<RefreshSession>().AsNoTracking()
            .Where(session => session.EndedAt == null && session.IdleExpiresAt > now && session.AbsoluteExpiresAt > now);
}
