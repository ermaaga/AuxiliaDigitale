using Auxilia.Api.Infrastructure;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Diagnostics;

namespace Auxilia.Api.Authorization;

/// <summary>
/// Endpoints of the signed-in tenant user without a permission (own profile, sessions, navigation, logout): a platform
/// token — console or tenant-scoped — is authenticated but is not a tenant user, so it gets 403 <c>AUX-12071</c> (D-21).
/// Endpoints with <see cref="PermissionEndpointExtensions.RequirePermission"/> need no filter: platform tokens hold no
/// tenant role.
/// </summary>
internal sealed class TenantUserEndpointFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        return context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>().ActorType == ActorType.User
            ? next(context)
            : ValueTask.FromResult<object?>(Errors.Identity.TenantUserRequired().ToProblem());
    }
}

/// <summary>Endpoint metadata: an endpoint of the signed-in tenant user, acting on the caller only (documentation, tests).</summary>
public sealed record TenantUserMetadata;

public static class TenantUserEndpointExtensions
{
    /// <summary>An authenticated tenant user (never a platform token); the handler acts on the caller only.</summary>
    public static TBuilder RequireTenantUser<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization()
            .AddEndpointFilter<TBuilder, TenantUserEndpointFilter>()
            .WithMetadata(new TenantUserMetadata());
}
