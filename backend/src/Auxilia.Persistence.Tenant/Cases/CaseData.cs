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
}
