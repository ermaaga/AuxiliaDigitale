using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Settings;

namespace Auxilia.Application.Tests.Configuration;

/// <summary>A cache that remembers entries by key until their tag is invalidated, and records what happened.</summary>
internal sealed class FakeReferenceDataCache : IReferenceDataCache
{
    private readonly Dictionary<string, (object Value, IReadOnlyList<string> Tags)> entries = new(StringComparer.Ordinal);

    public List<CacheEntry> Requests { get; } = [];

    public List<string> Invalidated { get; } = [];

    public int Loads { get; private set; }

    public async Task<T> GetOrCreateAsync<T>(CacheEntry entry, Func<CancellationToken, Task<T>> load, CancellationToken cancellationToken)
    {
        Requests.Add(entry);
        if (entries.TryGetValue(entry.Key, out var cached))
        {
            return (T)cached.Value;
        }

        Loads++;
        var value = await load(cancellationToken);
        entries[entry.Key] = (value!, entry.Tags);
        return value;
    }

    public Task InvalidateAsync(string tag, CancellationToken cancellationToken)
    {
        Invalidated.Add(tag);
        foreach (var key in entries.Where(item => item.Value.Tags.Contains(tag)).Select(item => item.Key).ToArray())
        {
            entries.Remove(key);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Reversible "encryption" that makes protected values recognisable.</summary>
internal sealed class FakeSecretProtector : ISettingSecretProtector
{
    public string Protect(string plainValue) => "enc:" + plainValue;

    public string Unprotect(string protectedValue) =>
        protectedValue.StartsWith("enc:", StringComparison.Ordinal)
            ? protectedValue[4..]
            : throw new System.Security.Cryptography.CryptographicException("not protected by this key ring");
}

/// <summary>In-memory stores of the three stored levels.</summary>
internal sealed class InMemorySettingStores : IPlatformSettingStore, ITenantSettingStore
{
    public Dictionary<string, string> Platform { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Tenant { get; } = new(StringComparer.Ordinal);

    public Dictionary<(Guid UserId, string Key), string> User { get; } = [];

    public Task<IReadOnlyDictionary<string, string>> GetValuesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Platform));

    public Task SetAsync(string key, string json, CancellationToken cancellationToken)
    {
        Platform[key] = json;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        Platform.Remove(key);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, string>> GetTenantValuesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(Tenant));

    public Task SetTenantValueAsync(string key, string json, CancellationToken cancellationToken)
    {
        Tenant[key] = json;
        return Task.CompletedTask;
    }

    public Task RemoveTenantValueAsync(string key, CancellationToken cancellationToken)
    {
        Tenant.Remove(key);
        return Task.CompletedTask;
    }

    public Task<string?> FindUserValueAsync(Guid userId, string key, CancellationToken cancellationToken) =>
        Task.FromResult(User.GetValueOrDefault((userId, key)));

    public Task SetUserValueAsync(Guid userId, string key, string json, CancellationToken cancellationToken)
    {
        User[(userId, key)] = json;
        return Task.CompletedTask;
    }

    public Task RemoveUserValueAsync(Guid userId, string key, CancellationToken cancellationToken)
    {
        User.Remove((userId, key));
        return Task.CompletedTask;
    }
}
