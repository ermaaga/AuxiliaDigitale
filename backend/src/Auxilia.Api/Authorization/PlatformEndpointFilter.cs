using System.Security.Claims;

using Auxilia.Api.Infrastructure;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.Infrastructure.Security.Tokens;

namespace Auxilia.Api.Authorization;

/// <summary>
/// Platform (System) endpoints (N02): a console token (platform, no tenant) for <c>/platform/…</c>, or a tenant-scoped
/// platform token for the technical endpoints of that tenant (any tenant status; business endpoints never accept
/// platform tokens, D-21). Otherwise 403 <c>AUX-12040</c>.
/// </summary>
internal sealed class PlatformEndpointFilter(bool tenantScoped) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var isPlatform = http.RequestServices.GetRequiredService<ICurrentUser>().ActorType == ActorType.Platform
            && http.User.FindFirstValue(TokenClaims.Scope) == TokenClaims.PlatformScope;
        var tokenTenant = http.User.FindFirstValue(TokenClaims.Tenant);
        var allowed = isPlatform && (tenantScoped
            ? tokenTenant is not null && http.RequestServices.GetRequiredService<ITenantContext>().Current is not null
            : tokenTenant is null);

        return allowed ? next(context) : ValueTask.FromResult<object?>(Errors.Identity.PlatformAccessRequired().ToProblem());
    }
}

public static class PlatformEndpointExtensions
{
    /// <summary>Console endpoints: a platform token without tenant.</summary>
    public static TBuilder RequirePlatformUser<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization().AddEndpointFilter<TBuilder>(new PlatformEndpointFilter(tenantScoped: false));

    /// <summary>Technical endpoints of one tenant: a platform token scoped to that tenant (opened from the console).</summary>
    public static TBuilder RequirePlatformTenant<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization().AddEndpointFilter<TBuilder>(new PlatformEndpointFilter(tenantScoped: true));
}
