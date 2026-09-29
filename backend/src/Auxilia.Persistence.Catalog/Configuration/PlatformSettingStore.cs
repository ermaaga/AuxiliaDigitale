using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Catalog.Configuration;

internal sealed class PlatformSettingStore : IPlatformSettingStore
{
    private readonly CatalogDbContext catalog;

    public PlatformSettingStore(CatalogDbContext catalog) => this.catalog = catalog;

    public async Task<IReadOnlyDictionary<string, string>> GetValuesAsync(CancellationToken cancellationToken) =>
        await catalog.PlatformSettings.AsNoTracking()
            .ToDictionaryAsync(setting => setting.Key, setting => setting.JsonValue, StringComparer.Ordinal, cancellationToken);

    public async Task SetAsync(string key, string json, CancellationToken cancellationToken)
    {
        var existing = await catalog.PlatformSettings.SingleOrDefaultAsync(setting => setting.Key == key, cancellationToken);
        if (existing is null)
        {
            catalog.PlatformSettings.Add(new PlatformSetting(key, json));
        }
        else
        {
            existing.SetValue(json);
        }

        await catalog.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        if (await catalog.PlatformSettings.SingleOrDefaultAsync(setting => setting.Key == key, cancellationToken) is { } existing)
        {
            catalog.PlatformSettings.Remove(existing);
            await catalog.SaveChangesAsync(cancellationToken);
        }
    }
}
