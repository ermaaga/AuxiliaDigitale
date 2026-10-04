using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Application.Scheduling;

/// <summary>Appointments (F13).</summary>
public sealed class SchedulingModule : IModuleDescriptor
{
    public const string ModuleCode = "scheduling";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Optional;

    public int EventCodeRangeStart => 15000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = SchedulingPermissions.All;

    public IReadOnlyList<SettingDefinition> Settings { get; } = [];

    public IReadOnlyList<NavigationEntry> Navigation { get; } =
    [
        new("appointments", "/appointments", "calendar", 50, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client], SchedulingPermissions.ViewAppointments),
    ];

    public IReadOnlyList<CustomFieldEntityDefinition> CustomFieldEntities { get; } = [new("appointment")];

    /// <summary>The appointment grid (F13, F21): the columns of the legacy grids; custom fields appended by the web app.</summary>
    public IReadOnlyList<GridDefinition> Grids { get; } =
    [
        new(
            "scheduling.appointments",
            [
                new("client", "Client", Sortable: true, Filterable: true, CanHide: false),
                new("employee", "Employee", Sortable: true, Filterable: true),
                new("startsAt", "DateTime", Sortable: true),
                new("duration", "DurationMinutes"),
                new("showInGlobalCalendar", "ShowInGlobalCalendar"),
                new("status", "Status", Sortable: true, Filterable: true),
            ],
            [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]),
    ];

    public void AddServices(IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<Abstractions.Exports.IExportSource, AppointmentExportSource>());
        services.TryAddScoped<AppointmentAccessPolicy>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceAccessPolicy<AppointmentResource>, AppointmentAccessPolicy>(
            provider => provider.GetRequiredService<AppointmentAccessPolicy>()));
        services.TryAddScoped<IAppointmentManager, AppointmentManager>();
        services.TryAddScoped<IAppointmentQueryService, AppointmentQueryService>();
    }
}
