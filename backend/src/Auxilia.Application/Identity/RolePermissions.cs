using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Contracts.Identity;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Identity;

/// <summary>
/// Permissions of the tenant roles (F22, D-18), changed by the System from the console. A change applies at once:
/// permissions are resolved per request from the cached grants (never in the token).
/// </summary>
public interface IRolePermissionManager
{
    /// <summary>The role holds exactly <paramref name="permissions"/> afterwards.</summary>
    Task<Result> SetAsync(string role, IReadOnlyList<string>? permissions, CancellationToken cancellationToken);

    /// <summary>The role holds the default permissions of the modules again (what a new tenant gets).</summary>
    Task<Result> ResetAsync(string role, CancellationToken cancellationToken);
}

public interface IRolePermissionQueryService
{
    /// <summary>Every declared permission, in module order, with the roles holding it.</summary>
    Task<IReadOnlyList<RolePermissionResponse>> ListAsync(CancellationToken cancellationToken);
}

internal sealed class RolePermissionManager(
    IOperationRunner operations,
    IRolePermissionDataFactory data,
    IModuleRegistry modules,
    ITenantContext tenantContext,
    IReferenceDataCache cache,
    ILogger<RolePermissionManager> logger) : IRolePermissionManager
{
    public Task<Result> SetAsync(string role, IReadOnlyList<string>? permissions, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.SetRolePermissions, new { Role = role }, async scope =>
        {
            if (!TenantRoles.TryParse(role, out var tenantRole))
            {
                return Errors.Identity.RolePermissionsInvalid("role", "validation.rolePermissions.role");
            }

            if (permissions is null || permissions.Any(code => code is null || !modules.PermissionModules.ContainsKey(code)))
            {
                return Errors.Identity.RolePermissionsInvalid("permissions", "validation.rolePermissions.unknown");
            }

            await ApplyAsync(scope, tenantRole, permissions.ToHashSet(StringComparer.Ordinal), cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> ResetAsync(string role, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.ResetRolePermissions, new { Role = role }, async scope =>
        {
            if (!TenantRoles.TryParse(role, out var tenantRole))
            {
                return Errors.Identity.RolePermissionsInvalid("role", "validation.rolePermissions.role");
            }

            var defaults = modules.All
                .SelectMany(module => module.Permissions)
                .Where(permission => permission.DefaultRoles.Contains(tenantRole))
                .Select(permission => permission.Code)
                .ToHashSet(StringComparer.Ordinal);
            await ApplyAsync(scope, tenantRole, defaults, cancellationToken);
            return Result.Success();
        }, cancellationToken);

    private async Task ApplyAsync(IOperationScope scope, TenantRole role, HashSet<string> desired, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        var current = await store.GrantsAsync(role, cancellationToken);
        var revoked = current.Where(grant => !desired.Contains(grant.PermissionCode)).ToArray();
        var granted = desired.Where(code => current.All(grant => grant.PermissionCode != code)).Order(StringComparer.Ordinal).ToArray();
        if (revoked.Length == 0 && granted.Length == 0)
        {
            return;
        }

        foreach (var grant in revoked)
        {
            store.Remove(grant);
        }

        foreach (var code in granted)
        {
            store.Add(new RoleGrant(role, code));
        }

        await store.SaveChangesAsync(cancellationToken);
        Log.Security.RolePermissionsChanged(
            logger,
            role.ToString(),
            string.Join(',', granted),
            string.Join(',', revoked.Select(grant => grant.PermissionCode).Order(StringComparer.Ordinal)));

        var slug = tenantContext.Tenant.Slug;
        scope.OnCommitted(ct => cache.InvalidateAsync(CacheTags.Tenant(slug, IdentityModule.ModuleCode), ct));
    }
}

internal sealed class RolePermissionQueryService(IModuleRegistry modules, RolePermissionsCache grants) : IRolePermissionQueryService
{
    public async Task<IReadOnlyList<RolePermissionResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var current = await grants.GetAsync(cancellationToken);
        var held = Enum.GetValues<TenantRole>().ToDictionary(role => role, role => current.Of(role).ToHashSet(StringComparer.Ordinal));
        return modules.All
            .SelectMany(module => module.Permissions.Select(permission => new RolePermissionResponse(
                permission.Code,
                module.Code,
                [.. Enum.GetValues<TenantRole>().Where(role => held[role].Contains(permission.Code)).Select(role => role.ToString())],
                [.. permission.DefaultRoles.Distinct().Order().Select(role => role.ToString())])))
            .ToArray();
    }
}
