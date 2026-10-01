using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Abstractions.Identity;

/// <summary>Role grants of the current tenant (<c>identity.role_permissions</c>), one unit of work (joins the running operation's transaction).</summary>
public interface IRolePermissionData : IAsyncDisposable
{
    /// <summary>The grants of the role (tracked).</summary>
    Task<IReadOnlyList<RoleGrant>> GrantsAsync(TenantRole role, CancellationToken cancellationToken);

    void Add(RoleGrant grant);

    void Remove(RoleGrant grant);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IRolePermissionDataFactory
{
    Task<IRolePermissionData> OpenAsync(CancellationToken cancellationToken);
}
