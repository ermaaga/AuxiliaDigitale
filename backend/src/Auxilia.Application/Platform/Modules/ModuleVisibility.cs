using Auxilia.Application.Abstractions.Modules;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Platform.Modules;

/// <summary>
/// ARCHITECTURE §5.2: a module is visible to role R in tenant T ⇔ it is Core, or (the tenant override if present,
/// otherwise the tenant plan) includes it for R. Permissions narrow it further per user (P2-03).
/// </summary>
internal static class ModuleVisibility
{
    public static readonly TenantRole[] AllRoles = Enum.GetValues<TenantRole>();

    public static TenantModules Compute(TenantModuleSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var overrides = source.Overrides.ToDictionary(item => item.ModuleCode, StringComparer.Ordinal);
        var modules = new Dictionary<string, TenantRole[]>(StringComparer.Ordinal);
        foreach (var module in source.Modules)
        {
            var roles = module.Kind == ModuleKind.Core
                ? AllRoles
                : overrides.TryGetValue(module.Code, out var tenantOverride)
                    ? tenantOverride.Roles
                    : source.PlanModules.GetValueOrDefault(module.Code, []);

            if (roles.Length > 0)
            {
                modules[module.Code] = roles.Distinct().Order().ToArray();
            }
        }

        return new TenantModules(modules);
    }
}
