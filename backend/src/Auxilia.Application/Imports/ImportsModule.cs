using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Application.Imports;

/// <summary>
/// Data imports from Excel (F19, D-18): technical, run by the System from the console with a tenant-scoped platform
/// token, so no permissions and no navigation. The entities come from the <c>IImportTarget</c>s of the modules.
/// </summary>
public sealed class ImportsModule : IModuleDescriptor
{
    public const string ModuleCode = "imports";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Core;

    public int EventCodeRangeStart => 22000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = [];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public IReadOnlyList<NavigationEntry> Navigation { get; } = [];

    public void AddServices(IServiceCollection services)
    {
        services.TryAddScoped<IImportTypeManager, ImportTypeManager>();
        services.TryAddScoped<IImportManager, ImportManager>();
        services.TryAddScoped<IImportQueryService, ImportQueryService>();
    }
}
