using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Application.Cases;

/// <summary>Service catalog, cases, payments, status history, service folders (F08–F11, F33).</summary>
public sealed class CasesModule : IModuleDescriptor
{
    public const string ModuleCode = "cases";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Optional;

    public int EventCodeRangeStart => 14000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = CasesPermissions.All;

    public IReadOnlyList<SettingDefinition> Settings { get; } = CasesSettings.All;

    public IReadOnlyList<NavigationEntry> Navigation { get; } =
    [
        new("cases", "/cases", "briefcase", 30, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client], CasesPermissions.ViewCases),
        new("services", "/services", "layers", 40, [TenantRole.Administrator], CasesPermissions.ManageServices),
    ];

    public IReadOnlyList<CustomFieldEntityDefinition> CustomFieldEntities { get; } = [new("case")];

    /// <summary>The service list (F08, F21): the columns of the legacy grid plus the category (Q26).</summary>
    public IReadOnlyList<GridDefinition> Grids { get; } =
    [
        // The case lists (F09, F21): the columns of the legacy grids plus the number; custom fields appended by the web app.
        new(
            "cases.cases",
            [
                new("number", "Number", Sortable: true),
                new("client", "Client", Sortable: true, Filterable: true, CanHide: false),
                new("service", "app.cases.service", Sortable: true, Filterable: true),
                new("startedOn", "StartDate", Sortable: true),
                new("expiresOn", "EndDate", Sortable: true),
                new("amountPaid", "AmountPaid", Sortable: true),
                new("status", "Status", Filterable: true),
                new("specialization", "Specialization", VisibleByDefault: false),
            ],
            [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]),

        new(
            "cases.services",
            [
                new("name", "Name", Sortable: true, Filterable: true, CanHide: false),
                new("description", "Description", VisibleByDefault: false),
                new("category", "app.services.category", Filterable: true),
                new("specialization", "Specialization", Filterable: true),
                new("price", "Price", Sortable: true),
                new("durationDays", "DurationDays", Sortable: true),
                new("status", "Status", Filterable: true),
            ],
            [TenantRole.Administrator]),
    ];

    public void AddServices(IServiceCollection services)
    {
        services.TryAddScoped<IServiceCatalogManager, ServiceCatalogManager>();
        services.TryAddScoped<IServiceCatalogQueryService, ServiceCatalogQueryService>();
        services.TryAddScoped<IServiceFolderManager, ServiceFolderManager>();
        services.TryAddScoped<IServiceFolderQueryService, ServiceFolderQueryService>();
        services.TryAddScoped<CaseAccessPolicy>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IResourceAccessPolicy<CaseResource>, CaseAccessPolicy>(provider => provider.GetRequiredService<CaseAccessPolicy>()));
        services.TryAddScoped<ICaseManager, CaseManager>();
        services.TryAddScoped<ICaseQueryService, CaseQueryService>();
    }
}
