using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Auxilia.Application.Directory;

/// <summary>Clients, employees, assignments, specializations, tags, consents, registration requests (F02–F06, F12).</summary>
public sealed class DirectoryModule : IModuleDescriptor
{
    public const string ModuleCode = "directory";

    public string Code => ModuleCode;

    public ModuleKind Kind => ModuleKind.Optional;

    public int EventCodeRangeStart => 13000;

    public IReadOnlyList<PermissionDefinition> Permissions { get; } = DirectoryPermissions.All;

    public IReadOnlyList<SettingDefinition> Settings { get; } = DirectorySettings.All;

    public IReadOnlyList<NavigationEntry> Navigation { get; } =
    [
        new("clients", "/clients", "users", 10, [TenantRole.Administrator, TenantRole.Employee], DirectoryPermissions.ViewClients),
        new("employees", "/employees", "user-cog", 20, [TenantRole.Administrator], DirectoryPermissions.ViewEmployees),
    ];

    public IReadOnlyList<CustomFieldEntityDefinition> CustomFieldEntities { get; } = [new(ClientRules.CustomFieldEntity)];

    /// <summary>The client lists (F05, F21): the columns of the legacy grid; custom fields are appended by the web app (F20).</summary>
    public IReadOnlyList<GridDefinition> Grids { get; } =
    [
        new(
            "directory.clients",
            [
                new("lastName", "Surname", Sortable: true, Filterable: true, CanHide: false),
                new("firstName", "Name"),
                new("email", "Email", Sortable: true, Filterable: true),
                new("userName", "Username", Sortable: true, Filterable: true, VisibleByDefault: false),
                new("phone", "Phone", Filterable: true),
                new("fiscalCode", "app.clients.fiscalCode", VisibleByDefault: false),
                new("employee", "app.clients.employee"),
                new("status", "Status", Filterable: true),
                new("canSignIn", "app.clients.canSignIn"),
            ],
            [TenantRole.Administrator, TenantRole.Employee]),

        // The employee list (F06): the columns of the legacy grid (the photo arrives with the profile image, B-03).
        new(
            "directory.employees",
            [
                new("lastName", "Surname", Sortable: true, Filterable: true, CanHide: false),
                new("firstName", "Name"),
                new("userName", "Username", Sortable: true, Filterable: true),
                new("email", "Email", Sortable: true, Filterable: true),
                new("phone", "Phone", Filterable: true),
                new("status", "Status", Filterable: true),
                new("specializations", "app.employees.specializations"),
                new("assignedClients", "app.employees.assignedClients"),
            ],
            [TenantRole.Administrator]),
    ];

    public void AddServices(IServiceCollection services)
    {
        services.TryAddScoped<ISpecializationManager, SpecializationManager>();
        services.TryAddScoped<ISpecializationQueryService, SpecializationQueryService>();
        services.TryAddScoped<IClientManager, ClientManager>();
        services.TryAddScoped<IClientQueryService, ClientQueryService>();
        services.TryAddScoped<IEmployeeManager, EmployeeManager>();
        services.TryAddScoped<IEmployeeQueryService, EmployeeQueryService>();
    }
}
