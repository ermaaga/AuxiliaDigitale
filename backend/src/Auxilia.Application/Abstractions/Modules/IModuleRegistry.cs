using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Abstractions.Modules;

/// <summary>The module descriptors of this deployment (unique codes and event ranges, checked at startup).</summary>
public interface IModuleRegistry
{
    IReadOnlyList<IModuleDescriptor> All { get; }

    IModuleDescriptor? Find(string code);
}

/// <summary>
/// Which modules the current tenant has and for which roles (ARCHITECTURE §5.2). Cached per tenant as
/// <c>t:{slug}:platform:modules:current</c> and evicted when the System changes plans or overrides.
/// </summary>
public interface IModuleAccess
{
    Task<TenantModules> GetAsync(CancellationToken cancellationToken);

    /// <summary>Whether the module is visible to at least one role of the current user in the current tenant.</summary>
    Task<bool> IsVisibleAsync(string moduleCode, CancellationToken cancellationToken);
}

/// <summary>Effective modules of a tenant: module code → roles that see it. Immutable, cached.</summary>
[System.ComponentModel.ImmutableObject(true)]
public sealed record TenantModules(IReadOnlyDictionary<string, TenantRole[]> Modules)
{
    public bool IsVisible(string moduleCode, IEnumerable<TenantRole> roles) =>
        Modules.TryGetValue(moduleCode, out var allowed) && roles.Any(allowed.Contains);
}

/// <summary>Catalog data needed to compute <see cref="TenantModules"/> (untracked reads).</summary>
public interface IModuleCatalogReader
{
    Task<TenantModuleSource> GetSourceAsync(Guid tenantId, DateTimeOffset at, CancellationToken cancellationToken);
}

/// <param name="Modules">Available modules of the catalog.</param>
/// <param name="PlanModules">Modules of the plan valid at the given instant (empty when the tenant has none).</param>
/// <param name="Overrides">Overrides of the tenant.</param>
public sealed record TenantModuleSource(
    IReadOnlyList<CatalogModule> Modules,
    IReadOnlyDictionary<string, TenantRole[]> PlanModules,
    IReadOnlyList<TenantModuleOverride> Overrides);

public sealed record CatalogModule(string Code, ModuleKind Kind);
