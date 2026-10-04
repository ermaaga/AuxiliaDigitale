using Auxilia.Application.Abstractions.Imports;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Imports;

/// <inheritdoc cref="IImportLookups"/>
#pragma warning disable CA1304, CA1311 // ToUpper() is translated to SQL upper(): no culture involved.
internal sealed class ImportLookups(ITenantDbContextFactory databases) : IImportLookups
{
    public async Task<IReadOnlySet<string>> TakenFiscalCodesAsync(IReadOnlyCollection<string> fiscalCodes, CancellationToken cancellationToken)
    {
        var wanted = Upper(fiscalCodes);
        await using var db = await databases.CreateAsync(cancellationToken);
        var taken = await db.Set<Person>().AsNoTracking()
            .Where(person => person.FiscalCode != null && wanted.Contains(person.FiscalCode.ToUpper()))
            .Select(person => person.FiscalCode!)
            .ToListAsync(cancellationToken);
        return taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlySet<string>> TakenUserNamesAsync(IReadOnlyCollection<string> userNames, CancellationToken cancellationToken)
    {
        var wanted = Upper(userNames);
        await using var db = await databases.CreateAsync(cancellationToken);
        var taken = await db.Set<User>().AsNoTracking()
            .Where(user => wanted.Contains(user.UserName.ToUpper()))
            .Select(user => user.UserName)
            .ToListAsync(cancellationToken);
        return taken.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, Guid>> EmployeesAsync(IReadOnlyCollection<string> userNames, CancellationToken cancellationToken)
    {
        var wanted = Upper(userNames);
        await using var db = await databases.CreateAsync(cancellationToken);
        var found = await db.Set<User>().AsNoTracking()
            .Where(user => user.IsActive && wanted.Contains(user.UserName.ToUpper())
                && EF.Property<List<UserRole>>(user, "roles").Any(role => role.Role == TenantRole.Employee))
            .Select(user => new { user.UserName, user.Id })
            .ToListAsync(cancellationToken);
        return found.ToDictionary(item => item.UserName, item => item.Id, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, Guid>> ClientsAsync(IReadOnlyCollection<string> fiscalCodes, CancellationToken cancellationToken)
    {
        var wanted = Upper(fiscalCodes);
        await using var db = await databases.CreateAsync(cancellationToken);
        var found = await (from person in db.Set<Person>().AsNoTracking()
                           join profile in db.Set<ClientProfile>() on person.Id equals profile.Id
                           where person.FiscalCode != null && wanted.Contains(person.FiscalCode.ToUpper())
                           select new { FiscalCode = person.FiscalCode!, person.Id })
            .ToListAsync(cancellationToken);
        return found.DistinctBy(item => item.FiscalCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.FiscalCode, item => item.Id, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, Guid>> ServicesAsync(IReadOnlyCollection<string> names, bool activeOnly, CancellationToken cancellationToken)
    {
        var wanted = Upper(names);
        await using var db = await databases.CreateAsync(cancellationToken);
        var found = await db.Set<Service>().AsNoTracking()
            .Where(service => (!activeOnly || service.IsActive) && wanted.Contains(service.Name.ToUpper()))
            .Select(service => new { service.Name, service.Id })
            .ToListAsync(cancellationToken);
        return found.DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Name, item => item.Id, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, Guid>> ServiceCategoriesAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        var wanted = Upper(names);
        await using var db = await databases.CreateAsync(cancellationToken);
        var found = await db.Set<ServiceCategory>().AsNoTracking()
            .Where(category => category.IsActive && wanted.Contains(category.Name.ToUpper()))
            .Select(category => new { category.Name, category.Id })
            .ToListAsync(cancellationToken);
        return found.DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Name, item => item.Id, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, Guid>> EmployeeSpecializationsAsync(IReadOnlyCollection<string> names, CancellationToken cancellationToken)
    {
        var wanted = Upper(names);
        await using var db = await databases.CreateAsync(cancellationToken);
        var found = await db.Set<Specialization>().AsNoTracking()
            .Where(specialization => specialization.IsActive && specialization.Role == TenantRole.Employee && wanted.Contains(specialization.Name.ToUpper()))
            .Select(specialization => new { specialization.Name, specialization.Id })
            .ToListAsync(cancellationToken);
        return found.DistinctBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(item => item.Name, item => item.Id, StringComparer.OrdinalIgnoreCase);
    }

    private static string[] Upper(IReadOnlyCollection<string> values) =>
        [.. values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal)];
}
#pragma warning restore CA1304, CA1311
