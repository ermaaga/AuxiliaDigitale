using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    public IReadOnlyList<CustomFieldEntityDefinition> CustomFieldEntities { get; } = [new("request")];

    /// <summary>The request inbox (F15, F21): the columns of the legacy grids.</summary>
    public IReadOnlyList<GridDefinition> Grids { get; } =
    [
        new(
            "engagement.requests",
            [
                new("sentAt", "CreatedAt", Sortable: true, CanHide: false),
                new("sender", "Name"),
                new("recipient", "app.requests.recipient"),
                new("type", "Type", Filterable: true),
                new("subject", "Subject"),
                new("status", "Status", Sortable: true, Filterable: true),
            ],
            [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]),
    ];

    public void AddServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Abstractions.Reporting.IDashboardContributor, EngagementDashboard>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Abstractions.Exports.IExportSource, RequestExportSource>());
        services.TryAddScoped<RequestAccessPolicy>();
        services.TryAddScoped<IRequestManager, RequestManager>();
        services.TryAddScoped<IRequestQueryService, RequestQueryService>();
        services.TryAddScoped<Public.INotificationSender, NotificationSender>();
        services.TryAddScoped<INotificationManager, NotificationManager>();
        services.TryAddScoped<INotificationQueryService, NotificationQueryService>();
    }
}
