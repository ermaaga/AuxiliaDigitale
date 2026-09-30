using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Application.Configuration;

/// <summary>Settings, branding, grids, custom fields (F20, F21, F23). Core; managed by the System from the console.</summary>
public sealed class ConfigurationModule : IModuleDescriptor
{
    public const string ModuleCode = "configuration";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Core;

    public int EventCodeRangeStart => 20000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = [];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public IReadOnlyList<NavigationEntry> Navigation { get; } = [];

    public void AddServices(IServiceCollection services)
    {
    }
}
