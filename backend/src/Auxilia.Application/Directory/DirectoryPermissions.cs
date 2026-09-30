using Auxilia.Application.Abstractions.Modules;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Directory;

/// <summary>Permissions of the directory module (<c>identity.role_permissions</c>, F22); default roles reproduce the legacy pages.</summary>
public static class DirectoryPermissions
{
    /// <summary>Client lists and details (own clients for employees, F05).</summary>
    public const string ViewClients = "directory.clients.view";

    /// <summary>Create, edit, activate and assign clients (F05).</summary>
    public const string ManageClients = "directory.clients.manage";

    /// <summary>Employee list and details (F06).</summary>
    public const string ViewEmployees = "directory.employees.view";

    /// <summary>Create and edit employees (F06).</summary>
    public const string ManageEmployees = "directory.employees.manage";

    /// <summary>Approve or reject registration requests (F03).</summary>
    public const string ReviewRegistrations = "directory.registrations.review";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(ViewClients, [TenantRole.Administrator, TenantRole.Employee]),
        new(ManageClients, [TenantRole.Administrator, TenantRole.Employee]),
        new(ViewEmployees, [TenantRole.Administrator]),
        new(ManageEmployees, [TenantRole.Administrator]),
        new(ReviewRegistrations, [TenantRole.Administrator, TenantRole.Employee]),
    ];
}
