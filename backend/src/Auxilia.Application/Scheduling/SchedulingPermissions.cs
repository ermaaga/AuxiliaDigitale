using Auxilia.Application.Abstractions.Modules;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Scheduling;

/// <summary>Permissions of the scheduling module (<c>identity.role_permissions</c>, F22); default roles reproduce the legacy pages.</summary>
public static class SchedulingPermissions
{
    /// <summary>Appointments and calendars (F13).</summary>
    public const string ViewAppointments = "scheduling.appointments.view";

    /// <summary>Create, request, move, approve and cancel appointments (F13).</summary>
    public const string ManageAppointments = "scheduling.appointments.manage";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(ViewAppointments, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]),
        new(ManageAppointments, [TenantRole.Administrator, TenantRole.Employee, TenantRole.Client]),
    ];
}
