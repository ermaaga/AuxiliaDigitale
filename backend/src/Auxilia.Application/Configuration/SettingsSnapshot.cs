using System.ComponentModel;

using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;

namespace Auxilia.Application.Configuration;

/// <summary>
/// Stored platform and tenant values (key → JSON) of one tenant; secrets stay protected. Cached as
/// <c>t:{slug}:configuration:snapshot:current</c> (ARCHITECTURE §7.3). Immutable, so the in-memory cache shares it.
/// </summary>
[ImmutableObject(true)]
public sealed record SettingsSnapshot(IReadOnlyDictionary<string, string> Platform, IReadOnlyDictionary<string, string> Tenant);

/// <summary>The settings snapshot of the current tenant (platform-only without a tenant).</summary>
internal sealed class SettingsSnapshotCache : ReferenceDataCache<SettingsSnapshot>
{
    public const string ModuleName = "configuration";

    private readonly IPlatformSettingStore platformSettings;
    private readonly ITenantSettingStore tenantSettings;

    public SettingsSnapshotCache(
        IReferenceDataCache cache,
        ITenantContext tenantContext,
        IPlatformSettingStore platformSettings,
        ITenantSettingStore tenantSettings)
        : base(cache, tenantContext)
    {
        this.platformSettings = platformSettings;
        this.tenantSettings = tenantSettings;
    }

    protected override string Module => ModuleName;

    protected override string Entity => "snapshot";

    protected override async Task<SettingsSnapshot> LoadAsync(TenantInfo? tenant, string variant, CancellationToken cancellationToken)
    {
        var platform = await platformSettings.GetValuesAsync(cancellationToken);
        var tenantValues = tenant is null
            ? new Dictionary<string, string>()
            : await tenantSettings.GetTenantValuesAsync(cancellationToken);

        return new SettingsSnapshot(platform, tenantValues);
    }
}
