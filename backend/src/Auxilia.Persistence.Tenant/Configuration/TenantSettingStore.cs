using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Configuration;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Configuration;

/// <summary>Tenant and user setting values through <see cref="ITenantDbContextFactory"/> (writes are audited).</summary>
internal sealed class TenantSettingStore : ITenantSettingStore
{
    private readonly ITenantDbContextFactory databases;

    public TenantSettingStore(ITenantDbContextFactory databases) => this.databases = databases;

    public async Task<IReadOnlyDictionary<string, string>> GetTenantValuesAsync(CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        return await db.Set<TenantSetting>().AsNoTracking()
            .ToDictionaryAsync(setting => setting.Key, setting => setting.JsonValue, StringComparer.Ordinal, cancellationToken);
    }

    public async Task SetTenantValueAsync(string key, string json, CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        var existing = await db.Set<TenantSetting>().SingleOrDefaultAsync(setting => setting.Key == key, cancellationToken);
        if (existing is null)
        {
            db.Set<TenantSetting>().Add(new TenantSetting(Guid.CreateVersion7(), key, json));
        }
        else
        {
            existing.SetValue(json);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveTenantValueAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        if (await db.Set<TenantSetting>().SingleOrDefaultAsync(setting => setting.Key == key, cancellationToken) is { } existing)
        {
            db.Set<TenantSetting>().Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<string?> FindUserValueAsync(Guid userId, string key, CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        return await db.Set<UserSetting>().AsNoTracking()
            .Where(setting => setting.UserId == userId && setting.Key == key)
            .Select(setting => setting.JsonValue)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SetUserValueAsync(Guid userId, string key, string json, CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        var existing = await db.Set<UserSetting>().SingleOrDefaultAsync(setting => setting.UserId == userId && setting.Key == key, cancellationToken);
        if (existing is null)
        {
            db.Set<UserSetting>().Add(new UserSetting(Guid.CreateVersion7(), userId, key, json));
        }
        else
        {
            existing.SetValue(json);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveUserValueAsync(Guid userId, string key, CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        if (await db.Set<UserSetting>().SingleOrDefaultAsync(setting => setting.UserId == userId && setting.Key == key, cancellationToken) is { } existing)
        {
            db.Set<UserSetting>().Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
