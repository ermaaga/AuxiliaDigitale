using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Cases;

internal sealed class ServiceCatalogDataFactory(ITenantDbContextFactory databases) : IServiceCatalogDataFactory
{
    public async Task<IServiceCatalogData> OpenAsync(CancellationToken cancellationToken) =>
        new ServiceCatalogData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IServiceCatalogData"/>
internal sealed class ServiceCatalogData(ITenantDbContext db) : IServiceCatalogData
{
    public async Task<IReadOnlyList<ServiceCategoryRow>> CategoriesAsync(CancellationToken cancellationToken)
    {
        var services = db.Set<Service>();
        return await db.Set<ServiceCategory>()
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new ServiceCategoryRow(category, services.Count(service => service.CategoryId == category.Id)))
            .ToListAsync(cancellationToken);
    }

    public Task<ServiceCategory?> FindCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<ServiceCategory>().SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

    // name is citext: equality is case-insensitive in the database.
    public Task<bool> CategoryNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        db.Set<ServiceCategory>().AnyAsync(category => category.IsActive && category.Name == name && category.Id != exceptId, cancellationToken);

    /// <summary>A single-entity query: <c>IgnoreQueryFilters</c> counts the deleted services too (the foreign key does).</summary>
    public Task<bool> CategoryInUseAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Service>().IgnoreQueryFilters().AnyAsync(service => service.CategoryId == id, cancellationToken);

    public async Task<(IReadOnlyList<ServiceRow> Items, int Total)> PageAsync(ServiceFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var services = db.Set<Service>().AsNoTracking();
        if (Pattern(filter.Name) is { } name)
        {
            services = services.Where(service => EF.Functions.ILike(service.Name, name, "\\"));
        }

        if (filter.CategoryId is { } categoryId)
        {
            services = services.Where(service => service.CategoryId == categoryId);
        }

        if (filter.SpecializationId is { } specializationId)
        {
            services = services.Where(service => service.SpecializationId == specializationId);
        }

        if (filter.Active is { } active)
        {
            services = services.Where(service => service.IsActive == active);
        }

        var total = await services.CountAsync(cancellationToken);
        var sorted = (filter.Sort, filter.Descending) switch
        {
            (ServiceSort.Price, false) => services.OrderBy(service => service.Price),
            (ServiceSort.Price, true) => services.OrderByDescending(service => service.Price),
            (ServiceSort.DurationDays, false) => services.OrderBy(service => service.DurationDays),
            (ServiceSort.DurationDays, true) => services.OrderByDescending(service => service.DurationDays),
            (_, false) => services.OrderBy(service => service.Name),
            (_, true) => services.OrderByDescending(service => service.Name),
        };

        var items = await WithNames(sorted.ThenBy(service => service.Id).Skip(filter.Skip).Take(filter.Take)).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<ServiceRow?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        WithNames(db.Set<Service>().AsNoTracking().Where(service => service.Id == id)).SingleOrDefaultAsync(cancellationToken);

    public Task<Service?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Service>().SingleOrDefaultAsync(service => service.Id == id, cancellationToken);

    public Task<bool> NameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        db.Set<Service>().AnyAsync(service => service.Name == name && service.Id != exceptId, cancellationToken);

    public Task<bool> ServiceHasCasesAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Case>().AnyAsync(@case => @case.ServiceId == id, cancellationToken);

    public Task<bool> IsActiveCategoryAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<ServiceCategory>().AnyAsync(category => category.Id == id && category.IsActive, cancellationToken);

    public Task<bool> IsActiveEmployeeSpecializationAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Specialization>().AnyAsync(
            specialization => specialization.Id == id && specialization.IsActive && specialization.Role == TenantRole.Employee, cancellationToken);

    public void Add(ServiceCategory category) => db.Set<ServiceCategory>().Add(category);

    public void Add(Service service) => db.Set<Service>().Add(service);

    public void Remove(ServiceCategory category) => db.Set<ServiceCategory>().Remove(category);

    public void Remove(Service service) => db.Set<Service>().Remove(service);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    /// <summary>The names of category and specialization (inactive ones included: the service keeps showing them).</summary>
    private IQueryable<ServiceRow> WithNames(IQueryable<Service> services)
    {
        var categories = db.Set<ServiceCategory>();
        var specializations = db.Set<Specialization>();
        return services.Select(service => new ServiceRow(
            service,
            categories.Where(category => category.Id == service.CategoryId).Select(category => category.Name).FirstOrDefault(),
            categories.Where(category => category.Id == service.CategoryId).Select(category => category.IsActive).FirstOrDefault(),
            specializations.Where(specialization => specialization.Id == service.SpecializationId).Select(specialization => specialization.Name).FirstOrDefault(),
            specializations.Where(specialization => specialization.Id == service.SpecializationId).Select(specialization => specialization.IsPrivate).FirstOrDefault()));
    }

    /// <summary>A "contains" ILIKE pattern with the wildcards of the text escaped; <c>null</c> for no filter.</summary>
    private static string? Pattern(string? text) =>
        string.IsNullOrWhiteSpace(text)
            ? null
            : "%" + text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
}
