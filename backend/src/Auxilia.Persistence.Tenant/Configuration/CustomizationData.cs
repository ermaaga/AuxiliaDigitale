using Auxilia.Application.Abstractions.Configuration;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Configuration;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Configuration;

internal sealed class CustomizationDataFactory(ITenantDbContextFactory databases) : ICustomizationDataFactory
{
    public async Task<ICustomizationData> OpenAsync(CancellationToken cancellationToken) =>
        new CustomizationData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="ICustomizationData"/>
internal sealed class CustomizationData(ITenantDbContext db) : ICustomizationData
{
    public async Task<IReadOnlyList<CustomFieldDefinition>> CustomFieldsAsync(string? entityType, CancellationToken cancellationToken) =>
        await db.Set<CustomFieldDefinition>()
            .Where(definition => entityType == null || definition.EntityType == entityType)
            .OrderBy(definition => definition.EntityType)
            .ThenBy(definition => definition.Order)
            .ThenBy(definition => definition.Label)
            .ToListAsync(cancellationToken);

    public Task<CustomFieldDefinition?> FindCustomFieldAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<CustomFieldDefinition>().SingleOrDefaultAsync(definition => definition.Id == id, cancellationToken);

    public void Add(CustomFieldDefinition definition) => db.Set<CustomFieldDefinition>().Add(definition);

    public void Remove(CustomFieldDefinition definition) => db.Set<CustomFieldDefinition>().Remove(definition);

    public async Task<IReadOnlyList<GridLayout>> LayoutsAsync(CancellationToken cancellationToken) =>
        await db.Set<GridLayout>().ToListAsync(cancellationToken);

    public Task<GridLayout?> FindLayoutAsync(string gridKey, string role, CancellationToken cancellationToken) =>
        db.Set<GridLayout>().SingleOrDefaultAsync(layout => layout.GridKey == gridKey && layout.Role == role, cancellationToken);

    public void Add(GridLayout layout) => db.Set<GridLayout>().Add(layout);

    public void Remove(GridLayout layout) => db.Set<GridLayout>().Remove(layout);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
