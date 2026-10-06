using Auxilia.Domain.Configuration;

namespace Auxilia.Application.Abstractions.Configuration;

/// <summary>
/// Custom field definitions and grid layouts of the current tenant, one unit of work (inside a write operation it joins
/// the operation's transaction). Dispose it at the end of the operation.
/// </summary>
public interface ICustomizationData : IAsyncDisposable
{
    /// <param name="entityType">One entity, or every entity when null.</param>
    Task<IReadOnlyList<CustomFieldDefinition>> CustomFieldsAsync(string? entityType, CancellationToken cancellationToken);

    Task<CustomFieldDefinition?> FindCustomFieldAsync(Guid id, CancellationToken cancellationToken);

    void Add(CustomFieldDefinition definition);

    void Remove(CustomFieldDefinition definition);

    Task<IReadOnlyList<GridLayout>> LayoutsAsync(CancellationToken cancellationToken);

    Task<GridLayout?> FindLayoutAsync(string gridKey, string role, CancellationToken cancellationToken);

    void Add(GridLayout layout);

    void Remove(GridLayout layout);

    /// <summary>The personal views of a user on a grid, by name.</summary>
    Task<IReadOnlyList<GridView>> ViewsAsync(Guid userId, string gridKey, bool readOnly, CancellationToken cancellationToken);

    void Add(GridView view);

    void Remove(GridView view);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ICustomizationDataFactory
{
    Task<ICustomizationData> OpenAsync(CancellationToken cancellationToken);
}
