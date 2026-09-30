using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Platform;

/// <summary>Reads of the platform console: the signed-in platform user and the tenant list (N02).</summary>
public interface IPlatformConsoleQueryService
{
    Task<Result<PlatformMeResponse>> GetMeAsync(CancellationToken cancellationToken);

    /// <summary>Every tenant but the archived ones, by slug.</summary>
    Task<IReadOnlyList<PlatformTenantResponse>> ListTenantsAsync(CancellationToken cancellationToken);
}

internal sealed class PlatformConsoleQueryService(ICurrentUser currentUser, IPlatformIdentityStore users, ICatalogStore catalog)
    : IPlatformConsoleQueryService
{
    public async Task<Result<PlatformMeResponse>> GetMeAsync(CancellationToken cancellationToken)
    {
        if (currentUser.ActorType != ActorType.Platform || currentUser.UserId is not { } userId
            || await users.FindUserAsync(userId, cancellationToken) is not { IsActive: true } user)
        {
            return Errors.Identity.UserNotFound();
        }

        return new PlatformMeResponse(user.Id, user.Email, user.DisplayName, user.Roles.Select(role => role.Role).Order(StringComparer.Ordinal).ToArray());
    }

    public async Task<IReadOnlyList<PlatformTenantResponse>> ListTenantsAsync(CancellationToken cancellationToken) =>
        (await catalog.ListTenantsAsync(cancellationToken))
            .Where(tenant => tenant.Status != Domain.Platform.TenantStatus.Archived)
            .OrderBy(tenant => tenant.Slug, StringComparer.Ordinal)
            .Select(tenant => new PlatformTenantResponse(tenant.Slug, tenant.DisplayName, tenant.Status.ToString(), tenant.SchemaVersion))
            .ToArray();
}
