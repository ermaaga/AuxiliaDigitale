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

    /// <summary>Assign or remove the employee in charge of a client (F05, legacy admin detail).</summary>
    public const string AssignClients = "directory.clients.assign";

    /// <summary>Delete clients (soft delete, Q29; legacy admin list).</summary>
    public const string DeleteClients = "directory.clients.delete";

    /// <summary>Reset a client's password: reset link or temporary password (F05, legacy admin detail).</summary>
    public const string ResetClientPasswords = "directory.clients.credentials";

    /// <summary>Employee list and details (F06).</summary>
    public const string ViewEmployees = "directory.employees.view";

    /// <summary>Create and edit employees (F06).</summary>
    public const string ManageEmployees = "directory.employees.manage";

    /// <summary>Approve or reject registration requests (F03).</summary>
    public const string ReviewRegistrations = "directory.registrations.review";

    /// <summary>Create, rename and delete the tags of the clients (N01, M-01).</summary>
    public const string ManageTags = "directory.tags.manage";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(ViewClients, [TenantRole.Administrator, TenantRole.Employee]),
        new(ManageClients, [TenantRole.Administrator, TenantRole.Employee]),
        new(AssignClients, [TenantRole.Administrator]),
        new(DeleteClients, [TenantRole.Administrator]),
        new(ResetClientPasswords, [TenantRole.Administrator]),
        new(ViewEmployees, [TenantRole.Administrator]),
        new(ManageEmployees, [TenantRole.Administrator]),
        new(ReviewRegistrations, [TenantRole.Administrator, TenantRole.Employee]),
        new(ManageTags, [TenantRole.Administrator]),
    ];
}
