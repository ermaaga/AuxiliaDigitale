using Auxilia.Application.Abstractions.Modules;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Reporting;

/// <summary>Permissions of the reporting module (<c>identity.role_permissions</c>, F22); default roles reproduce the legacy pages.</summary>
public static class ReportingPermissions
{
    /// <summary>The dashboard of the role (F27).</summary>
    public const string ViewDashboard = "reporting.dashboard.view";

    /// <summary>Export the lists the user sees (F26); each list still checks its own permission.</summary>
    public const string UseExports = "reporting.exports.use";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(ViewDashboard, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]),
        new(UseExports, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]),
    ];
}
