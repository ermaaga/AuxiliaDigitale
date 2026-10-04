using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Application.Reporting;

/// <summary>Dashboards, overviews and exports (F07, F26, F27); shares the 27000 range with the audit exports.</summary>
public sealed class ReportingModule : IModuleDescriptor
{
    public const string ModuleCode = "reporting";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Optional;

    public int EventCodeRangeStart => 27000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = ReportingPermissions.All;

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public IReadOnlyList<NavigationEntry> Navigation { get; } =
    [
        new("dashboard", "/dashboard", "layout-dashboard", 0, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client], ReportingPermissions.ViewDashboard),
    ];

    public void AddServices(IServiceCollection services)
    {
        services.TryAddScoped<IExportManager, ExportManager>();
        services.TryAddScoped<IExportQueryService, ExportQueryService>();
    }
}
