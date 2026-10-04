using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Application.Marketing;

/// <summary>Consents, segments, static lists, campaigns (N01).</summary>
public sealed class MarketingModule : IModuleDescriptor
{
    public const string ModuleCode = "marketing";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Optional;

    public int EventCodeRangeStart => 19000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = MarketingPermissions.All;

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public IReadOnlyList<NavigationEntry> Navigation { get; } =
    [
        new("marketing", "/marketing", "megaphone", 80, [TenantRole.Administrator, TenantRole.Employee], MarketingPermissions.ViewCampaigns),
    ];

    public void AddServices(IServiceCollection services)
    {
        services.TryAddScoped<AudiencePolicy>();
        services.TryAddScoped<ISegmentManager, SegmentManager>();
        services.TryAddScoped<ISegmentQueryService, SegmentQueryService>();
        services.TryAddScoped<IStaticListManager, StaticListManager>();
        services.TryAddScoped<IStaticListQueryService, StaticListQueryService>();
        services.TryAddScoped<Public.IAudienceResolver, Public.AudienceResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Abstractions.Imports.IImportTarget, ListMemberImportTarget>());
        services.TryAddScoped<IEmailTemplateManager, EmailTemplateManager>();
        services.TryAddScoped<IEmailTemplateQueryService, EmailTemplateQueryService>();
        services.TryAddScoped<ICampaignManager, CampaignManager>();
        services.TryAddScoped<ICampaignQueryService, CampaignQueryService>();
        services.TryAddScoped<ISuppressionManager, SuppressionManager>();
        services.TryAddScoped<ISuppressionQueryService, SuppressionQueryService>();
    }
}
