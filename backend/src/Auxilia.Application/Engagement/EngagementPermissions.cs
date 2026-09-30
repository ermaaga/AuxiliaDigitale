using Auxilia.Application.Abstractions.Modules;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Engagement;

/// <summary>Permissions of the engagement module (<c>identity.role_permissions</c>, F22); default roles reproduce the legacy pages.</summary>
public static class EngagementPermissions
{
    /// <summary>Requests sent and received (F15).</summary>
    public const string ViewRequests = "engagement.requests.view";

    /// <summary>Create, reply to and close requests (F15).</summary>
    public const string ManageRequests = "engagement.requests.manage";

    /// <summary>Delete requests (F15).</summary>
    public const string DeleteRequests = "engagement.requests.delete";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(ViewRequests, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]),
        new(ManageRequests, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]),
        new(DeleteRequests, [TenantRole.Administrator]),
    ];
}
