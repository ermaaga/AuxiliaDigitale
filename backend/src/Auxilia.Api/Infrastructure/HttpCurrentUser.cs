using System.Security.Claims;

using Auxilia.Application.Abstractions.Authorization;

namespace Auxilia.Api.Infrastructure;

/// <summary>
/// The caller of the current request, from the token claims (<c>sub</c>; <c>actor_type=platform</c> for System
/// console users). Tokens are issued by Identity (task P2-02); until then every caller is anonymous.
/// </summary>
internal sealed class HttpCurrentUser : ICurrentUser
{
    public const string ActorTypeClaim = "actor_type";

    public const string PlatformActor = "platform";

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
    }

    public ActorType ActorType { get; }

    public Guid? UserId { get; }
}
