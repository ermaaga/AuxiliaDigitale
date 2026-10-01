using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

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

    /// <summary>Grids (list pages) of the module whose columns the System lays out per role (F21).</summary>
    IReadOnlyList<GridDefinition> Grids => [];

    /// <summary>Entities of the module whose records carry custom fields defined by the System (F20).</summary>
    IReadOnlyList<CustomFieldEntityDefinition> CustomFieldEntities => [];

    /// <summary>Managers, QueryServices, validators, jobs of the module.</summary>
    void AddServices(IServiceCollection services);
}

/// <summary>
/// A permission of a module (<c>&lt;module&gt;.&lt;area&gt;.&lt;action&gt;</c>, e.g. <c>cases.cases.manage</c>);
/// <see cref="DefaultRoles"/> are granted in <c>identity.role_permissions</c> when a tenant first sees the permission
/// (synchronised at every tenant migration), reproducing the legacy per-role pages (F22).
/// </summary>
public sealed record PermissionDefinition(string Code, IReadOnlyList<TenantRole> DefaultRoles)
{
    public string DescriptionKey => $"permissions.{Code}.description";
}

/// <summary>
/// A link of the tenant app menu (<c>/me/navigation</c>): shown to <see cref="Roles"/> when the module is visible to the
/// role and the user has <see cref="Permission"/> (declared by the same module). <see cref="Route"/> is relative to <c>/{tenant}</c>.
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

/// <summary>
/// A grid of the tenant app (F21): key <c>&lt;module&gt;.&lt;grid&gt;</c>, its columns as the code renders them, and the
/// roles that see it. The System chooses, per role, which columns are shown and in which order; whether a column can be
/// sorted or filtered depends on the API, never on the layout.
/// </summary>
public sealed record GridDefinition(string Key, IReadOnlyList<GridColumnDefinition> Columns, IReadOnlyList<TenantRole> Roles)
{
    public string NameKey => $"grids.{Key}.name";
}

/// <param name="Key">Column id used by the web app (<c>attemptedAt</c>).</param>
/// <param name="LabelKey">Translation key of the header.</param>
/// <param name="Sortable">The API sorts by this column (<c>?sort=</c>).</param>
/// <param name="Filterable">The API filters by this column (<c>filter[…]</c>).</param>
/// <param name="CanHide">False for the column that identifies the row: always shown.</param>
public sealed record GridColumnDefinition(
    string Key,
    string LabelKey,
    bool Sortable = false,
    bool Filterable = false,
    bool CanHide = true,
    bool VisibleByDefault = true);

/// <summary>An entity whose records carry custom fields (<c>custom_fields jsonb</c>, F20), e.g. <c>client</c>.</summary>
public sealed record CustomFieldEntityDefinition(string Code)
{
    public string NameKey => $"customFields.entities.{Code}";
}
