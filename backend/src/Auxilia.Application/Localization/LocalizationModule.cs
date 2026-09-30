using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Application.Localization;

/// <summary>Languages and translations (F24). Core.</summary>
public sealed class LocalizationModule : IModuleDescriptor
{
    public const string ModuleCode = "localization";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Core;

    public int EventCodeRangeStart => 21000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = [];

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public IReadOnlyList<NavigationEntry> Navigation { get; } = [];

    public void AddServices(IServiceCollection services)
    {
    }
}
