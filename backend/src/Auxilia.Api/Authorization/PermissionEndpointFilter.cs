using Auxilia.Api.Infrastructure;
using Auxilia.Application.Abstractions.Authorization;

namespace Auxilia.Api.Authorization;

/// <summary>
/// The caller must hold the permission (effective permissions of <see cref="IPermissionAccess"/>), otherwise 403
/// <c>AUX-12028</c>. Runs after the tenant and module filters, so a hidden module stays a 404.
/// </summary>
internal sealed class PermissionEndpointFilter(string permission) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var guard = context.HttpContext.RequestServices.GetRequiredService<IAccessGuard>();
        var allowed = await guard.EnsureAsync(permission, context.HttpContext.RequestAborted);
        return allowed.IsSuccess ? await next(context) : allowed.Error!.ToProblem();
    }
}

/// <summary>Endpoint metadata: the permission an endpoint requires (documentation, tests).</summary>
public sealed record PermissionMetadata(string Permission);

public static class PermissionEndpointExtensions
{
    /// <summary>
    /// The endpoint needs an authenticated caller holding <paramref name="permission"/> (skill auxilia-security: every
    /// endpoint declares a permission or an explicit <c>AllowAnonymous</c>).
    /// </summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return builder
            .RequireAuthorization()
            .AddEndpointFilter<TBuilder>(new PermissionEndpointFilter(permission))
            .WithMetadata(new PermissionMetadata(permission));
    }
}
