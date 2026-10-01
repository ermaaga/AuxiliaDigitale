using Auxilia.Contracts.Platform;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Identity.Public;

/// <summary>
/// The Administrators of the current tenant as the System sees them (N02, technical endpoints and provisioning): the
/// first Administrator is created by the System and invited by e-mail only — the activation link never reaches the
/// System (D-21). Without a sending account (S-03) the invitation stays pending and can be sent again later.
/// </summary>
public interface ITenantAdministratorManager
{
    Task<IReadOnlyList<TenantAdministratorResponse>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Creates the first Administrator (the tenant must have none), then e-mails the invitation.</summary>
    Task<Result<TenantAdministratorInvitationResponse>> CreateInitialAsync(CreateTenantAdministratorRequest request, CancellationToken cancellationToken);

    /// <summary>E-mails a new activation link to an Administrator who has not activated the account yet.</summary>
    Task<Result<TenantAdministratorInvitationResponse>> SendInvitationAsync(Guid userId, CancellationToken cancellationToken);
}
