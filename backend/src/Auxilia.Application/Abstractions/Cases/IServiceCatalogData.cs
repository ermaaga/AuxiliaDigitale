using Auxilia.Domain.Cases;

namespace Auxilia.Application.Abstractions.Cases;

/// <summary>Sortable columns of the service list (legacy: name, price; default name).</summary>
public enum ServiceSort
{
    Name,
    Price,
    DurationDays,
}

/// <summary>A page request of the service list, already validated; <see cref="Name"/> matches anywhere, case-insensitive.</summary>
public sealed record ServiceFilter(
    string? Name,
    Guid? CategoryId,
    Guid? SpecializationId,
    bool? Active,
    ServiceSort Sort,
    bool Descending,
    int Skip,
    int Take);

/// <summary>A service with the names of its category and specialization.</summary>
public sealed record ServiceRow(
    Service Service,
    string? CategoryName,
    bool CategoryActive,
    string? SpecializationName,
    bool SpecializationPrivate);

/// <summary>A category with the number of services (not deleted) that use it.</summary>
public sealed record ServiceCategoryRow(ServiceCategory Category, int ServiceCount);

/// <summary>
/// The service catalog of the current tenant (F08): categories and services, one unit of work (inside a write operation
/// it joins the operation's transaction). Deleted services are never returned.
/// </summary>
public interface IServiceCatalogData : IAsyncDisposable
{
    /// <summary>Every category by name (inactive ones included).</summary>
    Task<IReadOnlyList<ServiceCategoryRow>> CategoriesAsync(CancellationToken cancellationToken);

    /// <summary>The category (tracked).</summary>
    Task<ServiceCategory?> FindCategoryAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Another active category has the name (case-insensitive).</summary>
    Task<bool> CategoryNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>A service, deleted ones included (their rows keep the reference), uses the category.</summary>
    Task<bool> CategoryInUseAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ServiceRow> Items, int Total)> PageAsync(ServiceFilter filter, CancellationToken cancellationToken);

    /// <summary>The service (untracked) with its names; <c>null</c> when it does not exist or is deleted.</summary>
    Task<ServiceRow?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The service (tracked).</summary>
    Task<Service?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Another service not deleted has the name (case-insensitive).</summary>
    Task<bool> NameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>The category exists and is active.</summary>
    Task<bool> IsActiveCategoryAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>An active specialization of the Employee role (Directory, F12) has the id.</summary>
    Task<bool> IsActiveEmployeeSpecializationAsync(Guid id, CancellationToken cancellationToken);

    void Add(ServiceCategory category);

    void Add(Service service);

    void Remove(ServiceCategory category);

    /// <summary>Soft delete (Q28).</summary>
    void Remove(Service service);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IServiceCatalogDataFactory
{
    Task<IServiceCatalogData> OpenAsync(CancellationToken cancellationToken);
}
