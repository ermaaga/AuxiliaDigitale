using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Application.Abstractions.Modules;

/// <summary>
/// A pluggable module (ARCHITECTURE §5.1): its catalog entry (<c>catalog.modules</c>), permissions, settings,
/// navigation and services. Adding a module = one descriptor + the module folders; the HTTP side is an
/// <c>IModuleEndpoints</c> in the Api with the same <see cref="Code"/> (the Application layer does not know HTTP).
/// Recurring jobs are registered in <see cref="AddServices"/> as <c>IRecurringJob</c> (run manually, D-15).
/// </summary>
public interface IModuleDescriptor
{
    /// <summary>Lower-case, stable code (<c>cases</c>, <c>marketing</c>): key of <c>catalog.modules</c> and of cache tags.</summary>
    string Code { get; }

    /// <summary>Core modules are visible to every role of every tenant; optional ones follow plan and overrides.</summary>
    ModuleKind Kind { get; }

    /// <summary>Start of the module's <c>EventCodes</c> range (e.g. 14000 for Cases).</summary>
    int EventCodeRangeStart { get; }

    /// <summary>Translation key of the module name (System console).</summary>
    string NameKey => $"modules.{Code}.name";

    IReadOnlyList<PermissionDefinition> Permissions { get; }

    IReadOnlyList<SettingDefinition> Settings { get; }

    IReadOnlyList<NavigationEntry> Navigation { get; }

    /// <summary>Managers, QueryServices, validators, jobs of the module.</summary>
    void AddServices(IServiceCollection services);
}

/// <summary>
/// A permission of a module (<c>cases.manage</c>); <see cref="DefaultRoles"/> seeds <c>identity.role_permissions</c>
/// (P2-03), which reproduces the legacy per-role pages (F22).
/// </summary>
public sealed record PermissionDefinition(string Code, IReadOnlyList<TenantRole> DefaultRoles)
{
    public string DescriptionKey => $"permissions.{Code}.description";
}

/// <summary>
/// A link of the tenant app menu (<c>/me/navigation</c>): shown to <see cref="Roles"/> when the module is visible to the
/// role and, from P2-03, the user has <see cref="Permission"/>. <see cref="Route"/> is relative to <c>/{tenant}</c>.
/// </summary>
public sealed record NavigationEntry(
    string Key,
    string Route,
    string Icon,
    int Order,
    IReadOnlyList<TenantRole> Roles,
    string? Permission = null)
{
    public string LabelKey => $"nav.{Key}";
}
