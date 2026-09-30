using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Localization.Public;
using Auxilia.Domain.Platform;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Application.Localization;

/// <summary>
/// Languages and translations (F24). Core. Bundles are public per tenant (<c>/i18n</c>); keys and translations are
/// edited only by the System with a tenant-scoped platform token (D-18), so the module declares no tenant permission.
/// </summary>
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
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<LanguagesCache>();
        services.TryAddScoped<TranslationBundleCache>();
        services.TryAddScoped<ILocalizationQueryService, LocalizationQueryService>();
        services.TryAddScoped<IResourceKeyManager, ResourceKeyManager>();
        services.TryAddScoped<ILocalizer, Localizer>();
    }
}
