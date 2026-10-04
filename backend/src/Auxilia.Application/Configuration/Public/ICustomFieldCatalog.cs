namespace Auxilia.Application.Configuration.Public;

/// <summary>A boolean custom field shown as a dashboard counter (F27, Q41: CAF, PATRONATO…).</summary>
public sealed record DashboardCounterField(string Key, string Label);

/// <summary>The custom field definitions other modules read (from the same cache as the validator).</summary>
public interface ICustomFieldCatalog
{
    /// <summary>The boolean fields of the entity flagged as dashboard counters, in their order.</summary>
    Task<IReadOnlyList<DashboardCounterField>> DashboardCountersAsync(string entityType, CancellationToken cancellationToken);
}

internal sealed class CustomFieldCatalog(CustomFieldCache cache) : ICustomFieldCatalog
{
    public async Task<IReadOnlyList<DashboardCounterField>> DashboardCountersAsync(string entityType, CancellationToken cancellationToken) =>
        (await cache.GetAsync(cancellationToken))
            .Where(definition => definition.EntityType == entityType && definition.DashboardCounter && definition.Type == "Boolean")
            .OrderBy(definition => definition.Order)
            .Select(definition => new DashboardCounterField(definition.Key, definition.Label))
            .ToArray();
}
