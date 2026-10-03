using Auxilia.Application.Abstractions.Directory;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Directory;

internal sealed class RegistrationDataFactory(ITenantDbContextFactory databases) : IRegistrationDataFactory
{
    public async Task<IRegistrationData> OpenAsync(CancellationToken cancellationToken) =>
        new RegistrationData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IRegistrationData"/>
internal sealed class RegistrationData(ITenantDbContext db) : IRegistrationData
{
    public async Task<(IReadOnlyList<RegistrationRow> Items, int Total)> PageAsync(RegistrationFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var requests = db.Set<RegistrationRequest>().AsNoTracking();
        if (filter.Status is { } status)
        {
            requests = requests.Where(request => request.Status == status);
        }

        if (filter.From is { } from)
        {
            requests = requests.Where(request => request.RequestedAt >= from);
        }

        if (filter.Before is { } before)
        {
            requests = requests.Where(request => request.RequestedAt < before);
        }

        if (Pattern(filter.Search) is { } search)
        {
            requests = requests.Where(request =>
                EF.Functions.ILike(request.FirstName + " " + request.LastName, search, "\\")
                || EF.Functions.ILike(request.LastName + " " + request.FirstName, search, "\\")
                || EF.Functions.ILike(request.Email, search, "\\")
                || EF.Functions.ILike(request.FiscalCode, search, "\\"));
        }

        var total = await requests.CountAsync(cancellationToken);
        var sorted = (filter.Sort, filter.Descending) switch
        {
            (RegistrationSort.ProcessedAt, false) => requests.OrderBy(request => request.ProcessedAt),
            (RegistrationSort.ProcessedAt, true) => requests.OrderByDescending(request => request.ProcessedAt),
            (RegistrationSort.LastName, false) => requests.OrderBy(request => request.LastName).ThenBy(request => request.FirstName),
            (RegistrationSort.LastName, true) => requests.OrderByDescending(request => request.LastName).ThenByDescending(request => request.FirstName),
            (_, false) => requests.OrderBy(request => request.RequestedAt),
            (_, true) => requests.OrderByDescending(request => request.RequestedAt),
        };

        var items = await sorted.ThenBy(request => request.Id).Skip(filter.Skip).Take(filter.Take).ToListAsync(cancellationToken);
        return (await WithProcessorsAsync(items, cancellationToken), total);
    }

    public async Task<RegistrationRow?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Set<RegistrationRequest>().AsNoTracking().SingleOrDefaultAsync(request => request.Id == id, cancellationToken) is { } request
            ? (await WithProcessorsAsync([request], cancellationToken))[0]
            : null;

    public Task<RegistrationRequest?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<RegistrationRequest>().SingleOrDefaultAsync(request => request.Id == id, cancellationToken);

    public Task<bool> PendingExistsAsync(string email, CancellationToken cancellationToken) =>
        db.Set<RegistrationRequest>().AnyAsync(request => request.Status == RegistrationStatus.Pending && request.Email == email, cancellationToken);

    public async Task<bool> EmailRegisteredAsync(string email, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var pattern = Escape(email);
        return await db.Set<Person>().AnyAsync(person => person.Email != null && EF.Functions.ILike(person.Email, pattern, "\\"), cancellationToken)
            || await db.Set<User>().AnyAsync(
                user => EF.Functions.ILike(user.UserName, pattern, "\\") || (user.Email != null && EF.Functions.ILike(user.Email, pattern, "\\")), cancellationToken);
    }

    public void Add(RegistrationRequest request) => db.Set<RegistrationRequest>().Add(request);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    /// <summary>
    /// The names of the processors, deleted people included (the list keeps showing who it was). A separate query:
    /// <c>IgnoreQueryFilters</c> applies to a whole query.
    /// </summary>
    private async Task<IReadOnlyList<RegistrationRow>> WithProcessorsAsync(IReadOnlyList<RegistrationRequest> requests, CancellationToken cancellationToken)
    {
        var ids = requests.Select(request => request.ProcessedByUserId).OfType<Guid>().Distinct().ToArray();
        var names = ids.Length == 0
            ? []
            : await (
                    from user in db.Set<User>()
                    where ids.Contains(user.Id)
                    join person in db.Set<Person>().IgnoreQueryFilters() on user.PersonId equals person.Id
                    select new { user.Id, Name = person.FirstName + " " + person.LastName })
                .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);
        return requests
            .Select(request => new RegistrationRow(request, request.ProcessedByUserId is { } processor ? names.GetValueOrDefault(processor) : null))
            .ToArray();
    }

    /// <summary>A "contains" ILIKE pattern with the wildcards of the text escaped; <c>null</c> for no filter.</summary>
    private static string? Pattern(string? text) => string.IsNullOrWhiteSpace(text) ? null : "%" + Escape(text.Trim()) + "%";

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
