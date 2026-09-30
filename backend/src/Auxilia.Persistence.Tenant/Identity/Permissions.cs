using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Modules;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Auxilia.Persistence.Tenant.Identity;

internal sealed class RolePermissionReader(ITenantDbContextFactory databases) : IRolePermissionReader
{
    public async Task<RolePermissionGrants> ReadAsync(CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        var grants = await db.Set<RoleGrant>().AsNoTracking().ToListAsync(cancellationToken);
        return new RolePermissionGrants(grants
            .GroupBy(grant => grant.Role)
            .ToDictionary(group => group.Key, group => group.Select(grant => grant.PermissionCode).Order(StringComparer.Ordinal).ToArray()));
    }
}

/// <summary>
/// Aligns <c>identity.permissions</c> with the module descriptors at every tenant migration (new tenants too):
/// a permission seen for the first time is granted to its default roles; a permission no longer declared is removed
/// with its grants; grants of known permissions are never touched (the System's choices, S-01, and the legacy
/// import mapping, F22/Q40, stay). Idempotent.
/// </summary>
public sealed class PermissionSynchronizer
{
    private readonly IModuleRegistry registry;
    private readonly ILogger<PermissionSynchronizer> logger;

    public PermissionSynchronizer(IModuleRegistry registry, ILogger<PermissionSynchronizer> logger)
    {
        this.registry = registry;
        this.logger = logger;
    }

    public async Task ApplyAsync(TenantDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        var declared = registry.All
            .SelectMany(module => module.Permissions.Select(permission => (Module: module.Code, Permission: permission)))
            .ToDictionary(item => item.Permission.Code, StringComparer.Ordinal);
        var known = await db.Set<PermissionEntry>().ToListAsync(cancellationToken);
        var knownCodes = known.Select(permission => permission.Code).ToHashSet(StringComparer.Ordinal);

        var removed = known.Where(permission => !declared.ContainsKey(permission.Code)).ToArray();
        db.Set<PermissionEntry>().RemoveRange(removed);

        var added = 0;
        var granted = 0;
        foreach (var (code, (module, permission)) in declared)
        {
            if (knownCodes.Contains(code))
            {
                continue;
            }

            db.Set<PermissionEntry>().Add(new PermissionEntry(code, module));
            added++;
            foreach (var role in permission.DefaultRoles.Distinct())
            {
                db.Set<RoleGrant>().Add(new RoleGrant(role, code));
                granted++;
            }
        }

        if (added > 0 || removed.Length > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            Log.Identity.PermissionsSynchronized(logger, added, removed.Length, granted);
        }
    }
}
