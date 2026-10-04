using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Cases;

internal sealed class CaseDataFactory(ITenantDbContextFactory databases) : ICaseDataFactory
{
    public async Task<ICaseData> OpenAsync(CancellationToken cancellationToken) => new CaseData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="ICaseData"/>
internal sealed class CaseData(ITenantDbContext db) : ICaseData
{
    public async Task<(IReadOnlyList<CaseRow> Items, int Total)> PageAsync(CaseFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var specializations = db.Set<Specialization>();
        var cases = Scoped(filter.Scope);
        if (filter.OnlyHeldOrUnspecialized && filter.Scope.ClientId is null && filter.Scope.EmployeeUserId is { } heldBy)
        {
            cases = cases.Where(row => row.@case.SpecializationId == null
                || specializations.Any(item => item.Id == row.@case.SpecializationId && item.Members.Any(member => member.UserId == heldBy)));
        }

        if (filter.ClientId is { } onlyClient)
        {
            cases = cases.Where(row => row.@case.ClientId == onlyClient);
        }

        if (filter.ServiceId is { } onlyService)
        {
            cases = cases.Where(row => row.@case.ServiceId == onlyService);
        }

        if (filter.Status is { } status)
        {
            cases = cases.Where(row => row.@case.Status == status);
        }

        if (!filter.IncludeCompleted)
        {
            cases = cases.Where(row => row.@case.Status != CaseStatus.Completed);
        }

        if (Pattern(filter.ClientName) is { } clientName)
        {
            cases = cases.Where(row => EF.Functions.ILike(row.person.FirstName + " " + row.person.LastName, clientName, "\\")
                || EF.Functions.ILike(row.person.LastName + " " + row.person.FirstName, clientName, "\\"));
        }

        if (Pattern(filter.ServiceName) is { } serviceName)
        {
            cases = cases.Where(row => EF.Functions.ILike(row.service.Name, serviceName, "\\"));
        }

        var total = await cases.CountAsync(cancellationToken);
        var sorted = (filter.Sort, filter.Descending) switch
        {
            (CaseSort.Client, false) => cases.OrderBy(row => row.person.LastName).ThenBy(row => row.person.FirstName),
            (CaseSort.Client, true) => cases.OrderByDescending(row => row.person.LastName).ThenByDescending(row => row.person.FirstName),
            (CaseSort.Service, false) => cases.OrderBy(row => row.service.Name),
            (CaseSort.Service, true) => cases.OrderByDescending(row => row.service.Name),
            (CaseSort.ExpiresOn, false) => cases.OrderBy(row => row.@case.ExpiresOn),
            (CaseSort.ExpiresOn, true) => cases.OrderByDescending(row => row.@case.ExpiresOn),
            (CaseSort.AmountPaid, false) => cases.OrderBy(row => row.@case.Payments.Sum(payment => payment.Amount)),
            (CaseSort.AmountPaid, true) => cases.OrderByDescending(row => row.@case.Payments.Sum(payment => payment.Amount)),
            (CaseSort.Number, false) => cases.OrderBy(row => row.@case.Number),
            (CaseSort.Number, true) => cases.OrderByDescending(row => row.@case.Number),
            (_, false) => cases.OrderBy(row => row.@case.StartedOn).ThenBy(row => row.@case.Number),
            (_, true) => cases.OrderByDescending(row => row.@case.StartedOn).ThenByDescending(row => row.@case.Number),
        };

        var items = await sorted
            .ThenBy(row => row.@case.Id)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .Select(row => new CaseRow(
                row.@case.Id,
                row.@case.Number,
                row.@case.ClientId,
                row.person.FirstName + " " + row.person.LastName,
                row.@case.ServiceId,
                row.service.Name,
                row.@case.SpecializationId,
                specializations.Where(item => item.Id == row.@case.SpecializationId).Select(item => item.Name).FirstOrDefault(),
                specializations.Where(item => item.Id == row.@case.SpecializationId).Select(item => item.IsPrivate).FirstOrDefault(),
                row.@case.Status,
                row.@case.IsRejected,
                row.@case.IsActive,
                row.@case.StartedOn,
                row.@case.DueOn,
                row.@case.ExpiresOn,
                row.@case.Price,
                row.@case.Currency,
                row.@case.Payments.Sum(payment => payment.Amount),
                row.@case.CustomFields))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<CaseDashboard> DashboardAsync(CaseScope scope, DateOnly? from, DateOnly today, DateOnly horizon, int take, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var visible = Scoped(scope);
        var open = visible.Where(row => row.@case.IsActive && row.@case.Status != CaseStatus.Completed);
        var started = from is { } first ? visible.Where(row => row.@case.StartedOn >= first) : visible;

        var openCount = await open.CountAsync(cancellationToken);
        var perService = await started
            .GroupBy(row => row.service.Name)
            .Select(group => new { Service = group.Key, Count = group.Count() })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Service)
            .Take(20)
            .ToListAsync(cancellationToken);
        // Legacy: the money received of the cases started in each month (one row per payment, then grouped).
        var revenue = await started
            .SelectMany(row => row.@case.Payments.Select(payment => new { row.@case.StartedOn.Year, row.@case.StartedOn.Month, payment.Amount }))
            .GroupBy(item => new { item.Year, item.Month })
            .Select(group => new { group.Key.Year, group.Key.Month, Amount = group.Sum(item => item.Amount) })
            .OrderBy(item => item.Year)
            .ThenBy(item => item.Month)
            .ToListAsync(cancellationToken);
        var dueSoon = await open
            .Where(row => row.@case.DueOn != null && row.@case.DueOn >= today && row.@case.DueOn <= horizon)
            .OrderBy(row => row.@case.DueOn)
            .ThenBy(row => row.@case.Number)
            .Take(take)
            .Select(row => new CaseDashboardItem(row.@case.Id, row.@case.Number, row.person.FirstName + " " + row.person.LastName, row.service.Name, row.@case.DueOn))
            .ToListAsync(cancellationToken);
        var latest = await open
            .OrderByDescending(row => row.@case.StartedOn)
            .ThenByDescending(row => row.@case.Number)
            .Select(row => new CaseDashboardItem(row.@case.Id, row.@case.Number, row.person.FirstName + " " + row.person.LastName, row.service.Name, row.@case.DueOn))
            .FirstOrDefaultAsync(cancellationToken);

        return new CaseDashboard(
            openCount,
            perService.Select(item => (item.Service, item.Count)).ToArray(),
            revenue.Select(item => (item.Year, item.Month, item.Amount)).ToArray(),
            dueSoon,
            latest);
    }

    /// <summary>
    /// The cases a scope sees (F10, D-04): every case, the client's own, or for an employee those without a
    /// specialization, with a non-private one or with one held. Cases are the root and people are joined: their
    /// soft-delete filters apply (no deleted case, no deleted client).
    /// </summary>
    private IQueryable<CaseJoin> Scoped(CaseScope scope)
    {
        var specializations = db.Set<Specialization>();
        var cases =
            from @case in db.Set<Case>().AsNoTracking()
            join person in db.Set<Person>() on @case.ClientId equals person.Id
            join service in db.Set<Service>() on @case.ServiceId equals service.Id
            select new CaseJoin { @case = @case, person = person, service = service };

        if (scope.ClientId is { } clientId)
        {
            return cases.Where(row => row.@case.ClientId == clientId);
        }

        if (scope.EmployeeUserId is { } employeeUserId)
        {
            return cases.Where(row => row.@case.SpecializationId == null
                || specializations.Any(item => item.Id == row.@case.SpecializationId && (!item.IsPrivate || item.Members.Any(member => member.UserId == employeeUserId))));
        }

        return scope.Everything ? cases : cases.Where(_ => false);
    }

#pragma warning disable SA1300, IDE1006, CA1716 // Lower-case members keep the existing query shapes (`row.@case`, `row.person`).
    private sealed class CaseJoin
    {
        public Case @case { get; init; } = null!;

        public Person person { get; init; } = null!;

        public Service service { get; init; } = null!;
    }
#pragma warning restore SA1300, IDE1006, CA1716

    public async Task<int> NextNumberAsync(int year, CancellationToken cancellationToken) =>
        // The upsert locks the year's row until the operation's transaction ends: concurrent cases get distinct numbers.
        // Not composable SQL: read as a list.
        (await ((DbContext)db).Database
            .SqlQuery<int>($"""
                INSERT INTO cases.case_numbers (year, last_number) VALUES ({year}, 1)
                ON CONFLICT (year) DO UPDATE SET last_number = cases.case_numbers.last_number + 1
                RETURNING last_number AS "Value"
                """)
            .ToListAsync(cancellationToken)).Single();

    public Task<Case?> FindAsync(Guid id, bool readOnly, CancellationToken cancellationToken)
    {
        var cases = readOnly ? db.Set<Case>().AsNoTracking() : db.Set<Case>();
        return cases.SingleOrDefaultAsync(@case => @case.Id == id, cancellationToken);
    }

    /// <summary>Single-row lookups with <c>IgnoreQueryFilters</c> on their own: a deleted client or service keeps its name.</summary>
    public async Task<CaseNames> NamesAsync(Case @case, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@case);

        var client = await db.Set<Person>().IgnoreQueryFilters().AsNoTracking()
            .Where(person => person.Id == @case.ClientId)
            .Select(person => person.FirstName + " " + person.LastName)
            .SingleOrDefaultAsync(cancellationToken);
        var service = await db.Set<Service>().IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.Id == @case.ServiceId)
            .Select(item => new { item.Name, item.Description, item.DurationDays })
            .SingleAsync(cancellationToken);
        var specialization = @case.SpecializationId is { } specializationId
            ? await db.Set<Specialization>().AsNoTracking()
                .Where(item => item.Id == specializationId)
                .Select(item => new { item.Name, item.IsPrivate })
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        return new CaseNames(client ?? string.Empty, service.Name, service.Description, service.DurationDays, specialization?.Name, specialization?.IsPrivate ?? false);
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

    public Task<CaseService?> ServiceAsync(Guid serviceId, CancellationToken cancellationToken) =>
        db.Set<Service>().AsNoTracking()
            .Where(service => service.Id == serviceId)
            .Select(service => new CaseService(service.Id, service.Name, service.Price, service.Currency, service.SpecializationId, service.IsActive))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool?> EmployeeSpecializationPrivacyAsync(Guid specializationId, CancellationToken cancellationToken) =>
        await db.Set<Specialization>().AsNoTracking()
            .Where(specialization => specialization.Id == specializationId && specialization.IsActive && specialization.Role == TenantRole.Employee)
            .Select(specialization => (bool?)specialization.IsPrivate)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<bool> IsPrivateSpecializationAsync(Guid specializationId, CancellationToken cancellationToken) =>
        db.Set<Specialization>().AnyAsync(specialization => specialization.Id == specializationId && specialization.IsPrivate, cancellationToken);

    public async Task<CaseCaller> CallerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var personId = await db.Set<User>().Where(user => user.Id == userId).Select(user => (Guid?)user.PersonId).SingleOrDefaultAsync(cancellationToken);
        var specializations = await db.Set<Specialization>()
            .Where(specialization => specialization.Members.Any(member => member.UserId == userId))
            .Select(specialization => specialization.Id)
            .ToListAsync(cancellationToken);
        return new CaseCaller(personId, specializations.ToHashSet());
    }

    public Task<bool> HasOpenCasesAsync(Guid clientId, DateOnly today, CancellationToken cancellationToken) =>
        db.Set<Case>().AnyAsync(
            @case => @case.ClientId == clientId && @case.IsActive && @case.Status != CaseStatus.Completed && (@case.ExpiresOn == null || @case.ExpiresOn >= today),
            cancellationToken);

    public void Add(Case @case) => db.Set<Case>().Add(@case);

    public void Remove(Case @case) => db.Set<Case>().Remove(@case);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    /// <summary>A "contains" ILIKE pattern with the wildcards of the text escaped; <c>null</c> for no filter.</summary>
    private static string? Pattern(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
