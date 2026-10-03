using Auxilia.Application.Abstractions.Modules;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Cases;

/// <summary>Permissions of the cases module (<c>identity.role_permissions</c>, F22); default roles reproduce the legacy pages.</summary>
public static class CasesPermissions
{
    /// <summary>Cases (clients: only their own, resource policy F10).</summary>
    public const string ViewCases = "cases.cases.view";

    /// <summary>Create cases, record payments, advance and complete them (F09).</summary>
    public const string ManageCases = "cases.cases.manage";

    /// <summary>Read the service catalog: staff choose a service when they open a case (F08, F09).</summary>
    public const string ViewServices = "cases.services.view";

    /// <summary>Service catalog and categories (F08).</summary>
    public const string ManageServices = "cases.services.manage";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(ViewCases, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]),
        new(ManageCases, [TenantRole.Administrator, TenantRole.Employee]),
        new(ViewServices, [TenantRole.Administrator, TenantRole.Employee]),
        new(ManageServices, [TenantRole.Administrator]),
    ];
}
