namespace Auxilia.Application.Abstractions.Settings;

/// <summary>Level-2 values in <c>catalog.platform_settings</c> (key → JSON).</summary>
public interface IPlatformSettingStore
{
    Task<IReadOnlyDictionary<string, string>> GetValuesAsync(CancellationToken cancellationToken);

    /// <summary>Inserts or updates the value and saves.</summary>
    Task SetAsync(string key, string json, CancellationToken cancellationToken);

    /// <summary>Removes the value if stored and saves.</summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken);
}

/// <summary>
/// Level-3 (<c>configuration.settings</c>) and level-4 (<c>configuration.user_settings</c>) values of the current
/// tenant. Writes join the transaction of the running operation.
/// </summary>
public interface ITenantSettingStore
{
    Task<IReadOnlyDictionary<string, string>> GetTenantValuesAsync(CancellationToken cancellationToken);

    Task SetTenantValueAsync(string key, string json, CancellationToken cancellationToken);

    Task RemoveTenantValueAsync(string key, CancellationToken cancellationToken);

    Task<string?> FindUserValueAsync(Guid userId, string key, CancellationToken cancellationToken);

    Task SetUserValueAsync(Guid userId, string key, string json, CancellationToken cancellationToken);

    Task RemoveUserValueAsync(Guid userId, string key, CancellationToken cancellationToken);
}
