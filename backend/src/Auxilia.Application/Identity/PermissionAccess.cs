using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Caching;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Identity;

/// <summary>Role grants of the current tenant, cached (<c>t:{slug}:identity:role-permissions:current</c>).</summary>
internal sealed class RolePermissionsCache : ReferenceDataCache<RolePermissionGrants>
{
    public const string ModuleName = IdentityModule.ModuleCode;

    private readonly IRolePermissionReader reader;

    public RolePermissionsCache(IReferenceDataCache cache, ITenantContext tenantContext, IRolePermissionReader reader)
        : base(cache, tenantContext)
    {
        this.reader = reader;
    }

    protected override string Module => ModuleName;

    protected override string Entity => "role-permissions";

    protected override async Task<RolePermissionGrants> LoadAsync(TenantInfo? tenant, string variant, CancellationToken cancellationToken) =>
        tenant is null ? new RolePermissionGrants(new Dictionary<TenantRole, string[]>()) : await reader.ReadAsync(cancellationToken);
}

/// <inheritdoc cref="IPermissionAccess"/>
internal sealed class PermissionAccess : IPermissionAccess
{
    private readonly RolePermissionsCache grants;
    private readonly IModuleAccess modules;
    private readonly IModuleRegistry registry;
    private readonly ICurrentUser currentUser;
    private IReadOnlySet<string>? granted;

    public PermissionAccess(RolePermissionsCache grants, IModuleAccess modules, IModuleRegistry registry, ICurrentUser currentUser)
    {
        this.grants = grants;
        this.modules = modules;
        this.registry = registry;
        this.currentUser = currentUser;
    }

    public async Task<IReadOnlySet<string>> GetGrantedAsync(CancellationToken cancellationToken)
    {
        if (granted is not null)
        {
            return granted;
        }

        if (currentUser.ActorType != ActorType.User || currentUser.Roles.Count == 0)
        {
            return granted = new HashSet<string>(StringComparer.Ordinal);
        }

        var roleGrants = await grants.GetAsync(cancellationToken);
        var tenantModules = await modules.GetAsync(cancellationToken);
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in currentUser.Roles)
        {
            foreach (var permission in roleGrants.Of(role))
            {
                // Only declared permissions of modules visible to this role count (Q39: every role, not the first).
                if (registry.PermissionModules.TryGetValue(permission, out var module)
                    && tenantModules.Modules.TryGetValue(module, out var roles)
                    && roles.Contains(role))
                {
                    result.Add(permission);
                }
            }
        }

        return granted = result;
    }

    public async Task<bool> HasAsync(string permission, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        return (await GetGrantedAsync(cancellationToken)).Contains(permission);
    }
}

/// <inheritdoc cref="IAccessGuard"/>
internal sealed class AccessGuard : IAccessGuard
{
    private readonly IPermissionAccess permissions;
    private readonly ICurrentUser currentUser;
    private readonly IServiceProvider services;
    private readonly ILogger<AccessGuard> logger;

    public AccessGuard(IPermissionAccess permissions, ICurrentUser currentUser, IServiceProvider services, ILogger<AccessGuard> logger)
    {
        this.permissions = permissions;
        this.currentUser = currentUser;
        this.services = services;
        this.logger = logger;
    }

    public async Task<Result> EnsureAsync(string permission, CancellationToken cancellationToken)
    {
        if (await permissions.HasAsync(permission, cancellationToken))
        {
            return Result.Success();
        }

        Log.Security.PermissionDenied(logger, currentUser.UserId, permission, "MissingPermission");
        return Errors.Identity.PermissionDenied();
    }

    public async Task<Result> EnsureAsync<TResource>(string permission, TResource resource, CancellationToken cancellationToken)
    {
        var allowed = await EnsureAsync(permission, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var policies = services.GetServices<IResourceAccessPolicy<TResource>>().ToArray();
        if (policies.Length == 0)
        {
            throw new InvalidOperationException($"No IResourceAccessPolicy<{typeof(TResource).Name}> is registered.");
        }

        foreach (var policy in policies)
        {
            if (!await policy.CanAccessAsync(resource, permission, cancellationToken))
            {
                Log.Security.PermissionDenied(logger, currentUser.UserId, permission, "ResourcePolicy");
                return Errors.Identity.PermissionDenied();
            }
        }

        return Result.Success();
    }
}
