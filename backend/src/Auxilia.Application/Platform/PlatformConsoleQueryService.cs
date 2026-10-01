using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Platform.Modules;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Platform;

/// <summary>Reads of the platform console (N02): the signed-in platform user, tenants, plans and modules. No business data (D-21).</summary>
public interface IPlatformConsoleQueryService
{
    Task<Result<PlatformMeResponse>> GetMeAsync(CancellationToken cancellationToken);

    /// <summary>Tenants by slug with their current plan; archived ones only on request.</summary>
    Task<IReadOnlyList<PlatformTenantResponse>> ListTenantsAsync(bool includeArchived, CancellationToken cancellationToken);

    /// <summary>A tenant (archived included) with its current plan and its latest runs (provisioning progress).</summary>
    Task<Result<PlatformTenantDetailResponse>> GetTenantAsync(string slug, CancellationToken cancellationToken);

    /// <summary>The active plans with their modules per role.</summary>
    Task<IReadOnlyList<PlanResponse>> ListPlansAsync(CancellationToken cancellationToken);

    /// <summary>The available modules for the tenant: plan roles, override and effective roles (ARCHITECTURE §5.2).</summary>
    Task<Result<IReadOnlyList<TenantModuleResponse>>> GetTenantModulesAsync(string slug, CancellationToken cancellationToken);
}

internal sealed class PlatformConsoleQueryService(
    ICurrentUser currentUser,
    IPlatformIdentityStore users,
    ICatalogStore catalog,
    IModuleCatalogReader modules,
    TimeProvider timeProvider) : IPlatformConsoleQueryService
{
    /// <summary>Runs shown with a tenant: enough to follow a provisioning and the last migrations.</summary>
    public const int RecentRuns = 10;

    public async Task<Result<PlatformMeResponse>> GetMeAsync(CancellationToken cancellationToken)
    {
        if (currentUser.ActorType != ActorType.Platform || currentUser.UserId is not { } userId
            || await users.FindUserAsync(userId, cancellationToken) is not { IsActive: true } user)
        {
            return Errors.Identity.UserNotFound();
        }

        return new PlatformMeResponse(user.Id, user.Email, user.DisplayName, user.Roles.Select(role => role.Role).Order(StringComparer.Ordinal).ToArray());
    }

    public async Task<IReadOnlyList<PlatformTenantResponse>> ListTenantsAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        var plans = await catalog.CurrentPlanCodesAsync(timeProvider.GetUtcNow(), cancellationToken);
        return (await catalog.ListTenantsAsync(cancellationToken))
            .Where(tenant => includeArchived || tenant.Status != TenantStatus.Archived)
            .OrderBy(tenant => tenant.Slug, StringComparer.Ordinal)
            .Select(tenant => new PlatformTenantResponse(
                tenant.Slug, tenant.DisplayName, tenant.Status.ToString(), tenant.SchemaVersion, plans.GetValueOrDefault(tenant.Id)))
            .ToArray();
    }

    public async Task<Result<PlatformTenantDetailResponse>> GetTenantAsync(string slug, CancellationToken cancellationToken)
    {
        if (await catalog.FindTenantAsync(slug, cancellationToken) is not { } tenant)
        {
            return Errors.Tenancy.TenantNotFound();
        }

        var period = await catalog.CurrentTenantPlanAsync(tenant.Id, timeProvider.GetUtcNow(), cancellationToken);
        var plan = period is null ? null : await catalog.FindPlanAsync(period.PlanId, cancellationToken);
        var runs = await catalog.RecentRunsAsync(tenant.Id, RecentRuns, cancellationToken);

        return new PlatformTenantDetailResponse(
            tenant.Slug,
            tenant.DisplayName,
            tenant.Status.ToString(),
            tenant.SchemaVersion,
            tenant.DataVersion,
            tenant.DefaultLanguage,
            tenant.TimeZone,
            tenant.ArchivedAt,
            plan is null ? null : new TenantPlanResponse(plan.Code, plan.NameKey, period!.ValidFrom),
            runs.Select(run => new MigrationRunResponse(run.Kind.ToString(), run.Status.ToString(), run.StartedAt, run.FinishedAt, run.ErrorCode, run.Message)).ToArray());
    }

    public async Task<IReadOnlyList<PlanResponse>> ListPlansAsync(CancellationToken cancellationToken) =>
        (await catalog.ListPlansAsync(cancellationToken))
            .Where(plan => plan.IsActive)
            .Select(plan => new PlanResponse(
                plan.Code,
                plan.NameKey,
                plan.IsDefault,
                plan.Modules
                    .OrderBy(module => module.ModuleCode, StringComparer.Ordinal)
                    .Select(module => new PlanModuleResponse(module.ModuleCode, Names(module.Roles)))
                    .ToArray()))
            .ToArray();

    public async Task<Result<IReadOnlyList<TenantModuleResponse>>> GetTenantModulesAsync(string slug, CancellationToken cancellationToken)
    {
        if (await catalog.FindTenantAsync(slug, cancellationToken) is not { } tenant)
        {
            return Errors.Tenancy.TenantNotFound();
        }

        var source = await modules.GetSourceAsync(tenant.Id, timeProvider.GetUtcNow(), cancellationToken);
        var effective = ModuleVisibility.Compute(source);
        var names = (await catalog.ListModulesAsync(cancellationToken)).ToDictionary(module => module.Id, module => module.NameKey, StringComparer.Ordinal);
        var overrides = source.Overrides.ToDictionary(item => item.ModuleCode, StringComparer.Ordinal);

        return source.Modules
            .OrderBy(module => module.Kind)
            .ThenBy(module => module.Code, StringComparer.Ordinal)
            .Select(module => new TenantModuleResponse(
                module.Code,
                names.GetValueOrDefault(module.Code, module.Code),
                module.Kind.ToString(),
                Names(source.PlanModules.GetValueOrDefault(module.Code, [])),
                overrides.TryGetValue(module.Code, out var item) ? new ModuleOverrideResponse(item.IsEnabled, Names(item.Roles)) : null,
                Names(effective.RolesOf(module.Code))))
            .ToArray();
    }

    private static string[] Names(IEnumerable<TenantRole> roles) => roles.Order().Select(role => role.ToString()).ToArray();
}
