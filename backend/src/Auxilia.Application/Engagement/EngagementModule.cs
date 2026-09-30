using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;

namespace Auxilia.Application.Engagement;

/// <summary>Requests, notifications, activities, tasks (F15, F16); event codes 17000 (requests) and 18000 (notifications).</summary>
public sealed class EngagementModule : IModuleDescriptor
{
    public const string ModuleCode = "engagement";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Optional;

    public int EventCodeRangeStart => 17000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = EngagementPermissions.All;

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public IReadOnlyList<NavigationEntry> Navigation { get; } =
    [
        new("requests", "/requests", "message-square", 70, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client], EngagementPermissions.ViewRequests),
    ];

    public void AddServices(IServiceCollection services)
    {
    }
}
