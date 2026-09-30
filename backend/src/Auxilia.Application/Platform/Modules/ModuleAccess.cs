using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Platform.Modules;

/// <summary>Effective modules of the current tenant, cached (<c>t:{slug}:platform:modules:current</c>).</summary>
internal sealed class TenantModulesCache : ReferenceDataCache<TenantModules>
{
    public const string ModuleName = "platform";

    private readonly IModuleCatalogReader catalog;
    private readonly TimeProvider timeProvider;

    public TenantModulesCache(IReferenceDataCache cache, ITenantContext tenantContext, IModuleCatalogReader catalog, TimeProvider timeProvider)
        : base(cache, tenantContext)
    {
        this.catalog = catalog;
        this.timeProvider = timeProvider;
    }

    protected override string Module => ModuleName;

    protected override string Entity => "modules";

    protected override async Task<TenantModules> LoadAsync(TenantInfo? tenant, string variant, CancellationToken cancellationToken)
    {
        if (tenant is null)
        {
            return new TenantModules(new Dictionary<string, TenantRole[]>());
        }

        var source = await catalog.GetSourceAsync(tenant.Id, timeProvider.GetUtcNow(), cancellationToken);
        return ModuleVisibility.Compute(source);
    }
}

/// <inheritdoc cref="IModuleAccess"/>
internal sealed class ModuleAccess : IModuleAccess
{
    private readonly TenantModulesCache cache;
    private readonly ICurrentUser currentUser;

    public ModuleAccess(TenantModulesCache cache, ICurrentUser currentUser)
    {
        this.cache = cache;
        this.currentUser = currentUser;
    }

    public Task<TenantModules> GetAsync(CancellationToken cancellationToken) => cache.GetAsync(cancellationToken);

    public async Task<bool> IsVisibleAsync(string moduleCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleCode);

        if (currentUser.ActorType != ActorType.User || currentUser.Roles.Count == 0)
        {
            return false;
        }

        var modules = await cache.GetAsync(cancellationToken);
        return modules.IsVisible(moduleCode, currentUser.Roles);
    }
}
