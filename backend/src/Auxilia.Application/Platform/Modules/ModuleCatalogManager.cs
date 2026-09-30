using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Platform.Modules;

/// <summary>Aligns <c>catalog.modules</c> with the module descriptors (N02: the module catalog is generated from code).</summary>
public interface IModuleCatalogManager
{
    /// <summary>
    /// Inserts new modules and adds them to the default plan for every role; updates kind and event range of known
    /// ones; marks the modules no longer deployed as unavailable (rows are kept: plans and overrides reference them).
    /// Plan contents chosen by the System are never changed for modules already in the catalog. Run by
    /// <c>auxctl migrate catalog</c>.
    /// </summary>
    Task<Result<ModuleSyncReport>> SyncAsync(CancellationToken cancellationToken);
}

public sealed record ModuleSyncReport(IReadOnlyList<string> Added, IReadOnlyList<string> Unavailable);

internal sealed class ModuleCatalogManager : IModuleCatalogManager
{
    private readonly IOperationRunner operations;
    private readonly IModuleRegistry registry;
    private readonly ICatalogStore catalog;
    private readonly IReferenceDataCache cache;

    public ModuleCatalogManager(IOperationRunner operations, IModuleRegistry registry, ICatalogStore catalog, IReferenceDataCache cache)
    {
        this.operations = operations;
        this.registry = registry;
        this.catalog = catalog;
        this.cache = cache;
    }

    public Task<Result<ModuleSyncReport>> SyncAsync(CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Tenancy.SyncModules, null, async scope =>
        {
            var existing = (await catalog.ListModulesAsync(cancellationToken)).ToDictionary(module => module.Id, StringComparer.Ordinal);
            var added = new List<string>();

            foreach (var descriptor in registry.All)
            {
                if (existing.Remove(descriptor.Code, out var module))
                {
                    module.Update(descriptor.Kind, descriptor.NameKey, descriptor.EventCodeRangeStart);
                }
                else
                {
                    catalog.Add(new PlatformModule(descriptor.Code, descriptor.Kind, descriptor.NameKey, descriptor.EventCodeRangeStart));
                    added.Add(descriptor.Code);
                }
            }

            var unavailable = existing.Values.Where(module => module.IsAvailable).Select(module => module.Id).Order(StringComparer.Ordinal).ToArray();
            foreach (var module in existing.Values)
            {
                module.MarkUnavailable();
            }

            // Module rows first: plan_modules references them.
            await catalog.SaveChangesAsync(cancellationToken);

            if (added.Count > 0)
            {
                var plan = await catalog.FindPlanAsync(await catalog.DefaultPlanIdAsync(cancellationToken), cancellationToken)
                    ?? throw new InvalidOperationException("The default plan does not exist.");
                foreach (var code in added)
                {
                    plan.SetModule(code, ModuleVisibility.AllRoles);
                }

                await catalog.SaveChangesAsync(cancellationToken);
            }

            // Every tenant's effective modules depend on the catalog.
            scope.OnCommitted(ct => cache.InvalidateAsync(CacheTags.Platform(TenantModulesCache.ModuleName), ct));
            return Result.Success(new ModuleSyncReport(added, unavailable));
        }, cancellationToken);
}
