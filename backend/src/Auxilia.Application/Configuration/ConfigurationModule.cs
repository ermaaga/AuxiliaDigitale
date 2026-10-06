using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Application.Configuration;

/// <summary>Settings, branding, grids, custom fields (F20, F21, F23). Core; managed by the System from the console.</summary>
public sealed class ConfigurationModule : IModuleDescriptor
{
    public const string ModuleCode = "configuration";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Core;

    public int EventCodeRangeStart => 20000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = [];

    public IReadOnlyList<SettingDefinition> Settings { get; } = BrandingSettings.All;

    public IReadOnlyList<NavigationEntry> Navigation { get; } = [];

    public void AddServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<CustomFieldCache>();
        services.TryAddScoped<ICustomFieldManager, CustomFieldManager>();
        services.TryAddScoped<ICustomFieldQueryService, CustomFieldQueryService>();
        services.TryAddScoped<Public.ICustomFieldValidator, CustomFieldValidator>();
        services.TryAddScoped<Public.ICustomFieldCatalog, Public.CustomFieldCatalog>();
        services.TryAddScoped<GridLayoutCache>();
        services.TryAddScoped<IGridLayoutManager, GridLayoutManager>();
        services.TryAddScoped<IGridQueryService, GridQueryService>();
        services.TryAddScoped<IGridViewQueryService, GridViewQueryService>();
        services.TryAddScoped<IGridViewManager, GridViewManager>();
    }
}
