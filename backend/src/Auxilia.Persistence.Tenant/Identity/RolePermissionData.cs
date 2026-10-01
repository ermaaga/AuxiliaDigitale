using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Identity;

internal sealed class RolePermissionDataFactory(ITenantDbContextFactory databases) : IRolePermissionDataFactory
{
    public async Task<IRolePermissionData> OpenAsync(CancellationToken cancellationToken) =>
        new RolePermissionData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IRolePermissionData"/>
internal sealed class RolePermissionData(ITenantDbContext db) : IRolePermissionData
{
    public async Task<IReadOnlyList<RoleGrant>> GrantsAsync(TenantRole role, CancellationToken cancellationToken) =>
        await db.Set<RoleGrant>().Where(grant => grant.Role == role).ToListAsync(cancellationToken);

    public void Add(RoleGrant grant) => db.Set<RoleGrant>().Add(grant);

    public void Remove(RoleGrant grant) => db.Set<RoleGrant>().Remove(grant);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
