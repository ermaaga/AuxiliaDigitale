using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Abstractions.Authorization;

/// <summary>
/// Effective permissions of the current tenant user (F22, ARCHITECTURE §5.2): the union over the user's roles of the
/// role's grants (<c>identity.role_permissions</c>), keeping only permissions whose module is visible to that role in
/// the tenant. Permissions are never in the token: grants are cached per tenant and computed per request.
/// </summary>
public interface IPermissionAccess
{
    /// <summary>Empty for anonymous, platform and system actors.</summary>
    Task<IReadOnlySet<string>> GetGrantedAsync(CancellationToken cancellationToken);

    Task<bool> HasAsync(string permission, CancellationToken cancellationToken);
}

/// <summary>
/// Resource-based rule of an area (e.g. private cases, D-04; clients see only their own data): decides whether the
/// current user may act on one resource once the permission is granted. Registered in the module's
/// <c>AddServices</c>; every registered policy for the resource type must allow.
/// </summary>
public interface IResourceAccessPolicy<in TResource>
{
    Task<bool> CanAccessAsync(TResource resource, string permission, CancellationToken cancellationToken);
}

/// <summary>Authorization step of Managers and QueryServices: permission, then resource policies (403 <c>AUX-12028</c>).</summary>
public interface IAccessGuard
{
    Task<Result> EnsureAsync(string permission, CancellationToken cancellationToken);

    Task<Result> EnsureAsync<TResource>(string permission, TResource resource, CancellationToken cancellationToken);
}

/// <summary>Grants of each tenant role (cached snapshot of <c>identity.role_permissions</c>).</summary>
[System.ComponentModel.ImmutableObject(true)]
public sealed record RolePermissionGrants(IReadOnlyDictionary<TenantRole, string[]> Grants)
{
    public IReadOnlyCollection<string> Of(TenantRole role) => Grants.TryGetValue(role, out var granted) ? granted : [];
}

/// <summary>Reads the role grants of the current tenant (untracked).</summary>
public interface IRolePermissionReader
{
    Task<RolePermissionGrants> ReadAsync(CancellationToken cancellationToken);
}
