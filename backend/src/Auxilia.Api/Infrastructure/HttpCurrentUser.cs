using System.Security.Claims;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Api.Infrastructure;

/// <summary>
/// The caller of the current request, from the token claims (<c>sub</c>; <c>actor_type=platform</c> for System
/// console users; every <c>role</c> claim of a tenant user as <see cref="Roles"/>). Tokens are issued by Identity
/// (<c>/api/v1/auth/token</c>); without a valid bearer token the caller is anonymous.
/// </summary>
internal sealed class HttpCurrentUser : ICurrentUser
{
    public const string ActorTypeClaim = "actor_type";

    public const string PlatformActor = "platform";

    public const string RoleClaim = "role";

    public HttpCurrentUser(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            ActorType = ActorType.Anonymous;
            return;
        }

        UserId = Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : null;
        ActorType = principal.FindFirstValue(ActorTypeClaim) == PlatformActor ? ActorType.Platform : ActorType.User;
        if (ActorType == ActorType.User)
        {
            // Every role claim counts (the legacy used only the first one, Q39).
            Roles = principal.Claims
                .Where(claim => claim.Type is ClaimTypes.Role or RoleClaim)
                .Select(claim => Enum.TryParse<TenantRole>(claim.Value, ignoreCase: false, out var role) ? role : (TenantRole?)null)
                .OfType<TenantRole>()
                .Distinct()
                .ToArray();
        }
    }

    public ActorType ActorType { get; }

    public Guid? UserId { get; }

    public IReadOnlyCollection<TenantRole> Roles { get; } = [];
}
