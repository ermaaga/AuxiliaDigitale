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

    /// <summary>The user's own notifications and preferences (F16).</summary>
    public const string ViewNotifications = "engagement.notifications.view";

    /// <summary>Delete requests (F15).</summary>
    public const string DeleteRequests = "engagement.requests.delete";

    /// <summary>Tasks given to the user or created by them; Administrators every task (B-26).</summary>
    public const string ViewTasks = "engagement.tasks.view";

    /// <summary>Create, change, complete and delete tasks (B-26).</summary>
    public const string ManageTasks = "engagement.tasks.manage";

    /// <summary>The timeline of a client (B-26).</summary>
    public const string ViewActivities = "engagement.activities.view";

    /// <summary>Write notes, calls, meetings and e-mails on a client (B-26).</summary>
    public const string ManageActivities = "engagement.activities.manage";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(ViewRequests, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]),
        new(ManageRequests, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]),
        new(DeleteRequests, [TenantRole.Administrator]),
        new(ViewNotifications, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]),
        new(ViewTasks, [TenantRole.Administrator, TenantRole.Employee]),
        new(ManageTasks, [TenantRole.Administrator, TenantRole.Employee]),
        new(ViewActivities, [TenantRole.Administrator, TenantRole.Employee]),
        new(ManageActivities, [TenantRole.Administrator, TenantRole.Employee]),
    ];
}
