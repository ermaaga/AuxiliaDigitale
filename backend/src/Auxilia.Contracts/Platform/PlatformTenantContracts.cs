namespace Auxilia.Contracts.Platform;

/// <summary>
/// <c>POST /platform/tenants</c> (N02): creates the tenant and queues its provisioning. <see cref="Administrator"/> is
/// the first Administrator, invited by e-mail once the tenant is ready (the link never reaches the System, D-21).
/// </summary>
public sealed record CreatePlatformTenantRequest(
    string Slug, string DisplayName, string DefaultLanguage, string TimeZone, TenantAdministratorInvite? Administrator);

public sealed record TenantAdministratorInvite(string Email, string FirstName, string LastName);

/// <summary><c>PUT /platform/tenants/{slug}</c>: the slug and the language never change here.</summary>
public sealed record UpdatePlatformTenantRequest(string DisplayName, string TimeZone);

/// <summary><c>PUT /platform/tenants/{slug}/plan</c>: the new plan starts now.</summary>
public sealed record ChangeTenantPlanRequest(string PlanCode);

/// <summary>
/// <c>PUT /platform/tenants/{slug}/modules/{code}</c> (D-18): the override replaces the plan for that module;
/// enabled for <see cref="Roles"/> (at least one), or disabled for everyone.
/// </summary>
public sealed record SetModuleOverrideRequest(bool IsEnabled, IReadOnlyList<string> Roles);

public sealed record PlatformTenantDetailResponse(
    string Slug,
    string DisplayName,
    string Status,
    string? SchemaVersion,
    string? DataVersion,
    string DefaultLanguage,
    string TimeZone,
    DateTimeOffset? ArchivedAt,
    TenantPlanResponse? Plan,
    IReadOnlyList<MigrationRunResponse> Runs);

public sealed record TenantPlanResponse(string Code, string NameKey, DateTimeOffset ValidFrom);

/// <summary>A provisioning, migration or job run of the tenant (summary; details are in the logs).</summary>
public sealed record MigrationRunResponse(string Kind, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string? ErrorCode, string? Message);

public sealed record PlanResponse(string Code, string NameKey, bool IsDefault, IReadOnlyList<PlanModuleResponse> Modules);

/// <summary>A module of a plan and the roles it is included for.</summary>
public sealed record PlanModuleResponse(string Code, IReadOnlyList<string> Roles);

/// <summary>
/// A module for one tenant: what the plan gives, the override (if any) and the roles that see it
/// (ARCHITECTURE §5.2; Core modules are visible to every role).
/// </summary>
public sealed record TenantModuleResponse(
    string Code, string NameKey, string Kind, IReadOnlyList<string> PlanRoles, ModuleOverrideResponse? Override, IReadOnlyList<string> EffectiveRoles);

public sealed record ModuleOverrideResponse(bool IsEnabled, IReadOnlyList<string> Roles);

/// <summary>An Administrator account of the tenant (technical endpoint; no business data, D-21).</summary>
public sealed record TenantAdministratorResponse(Guid UserId, string UserName, string? Email, bool IsActive, bool IsActivated);

/// <summary><c>POST /administrators</c>: the first Administrator of the tenant.</summary>
public sealed record CreateTenantAdministratorRequest(string Email, string FirstName, string LastName);

/// <summary>
/// Outcome of an invitation: e-mailed, or pending with the reason (e.g. the tenant has no sending account yet) —
/// the System can send it again later.
/// </summary>
public sealed record TenantAdministratorInvitationResponse(Guid UserId, bool InvitationSent, string? InvitationErrorCode);
