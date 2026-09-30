using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Contracts.Identity;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Identity;

/// <summary>The signed-in tenant user (<c>GET /me</c>).</summary>
public interface ICurrentUserQueryService
{
    /// <returns><c>AUX-12006</c> when the caller is not a tenant user or the account no longer exists or is inactive.</returns>
    Task<Result<MeResponse>> GetAsync(CancellationToken cancellationToken);
}

internal sealed class CurrentUserQueryService : ICurrentUserQueryService
{
    private readonly ICurrentUser currentUser;
    private readonly IIdentityDataFactory data;
    private readonly IPermissionAccess permissions;
    private readonly ITenantContext tenantContext;

    public CurrentUserQueryService(ICurrentUser currentUser, IIdentityDataFactory data, IPermissionAccess permissions, ITenantContext tenantContext)
    {
        this.currentUser = currentUser;
        this.data = data;
        this.permissions = permissions;
        this.tenantContext = tenantContext;
    }

    public async Task<Result<MeResponse>> GetAsync(CancellationToken cancellationToken)
    {
        if (currentUser.ActorType != ActorType.User || currentUser.UserId is not { } userId)
        {
            return Errors.Identity.UserNotFound();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var user = await store.FindAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Errors.Identity.UserNotFound();
        }

        // Roles come from the token (as every authorization decision in this request); they match the account until
        // the next refresh, which a role change forces (security stamp).
        var granted = await permissions.GetGrantedAsync(cancellationToken);
        return new MeResponse(
            user.Id,
            user.UserName,
            user.Email,
            user.LanguageCode,
            tenantContext.Tenant.Slug,
            currentUser.Roles.Order().Select(role => role.ToString()).ToArray(),
            granted.Order(StringComparer.Ordinal).ToArray());
    }
}
