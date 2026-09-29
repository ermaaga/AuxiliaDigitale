using Auxilia.Api.Infrastructure;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;

namespace Auxilia.Api.Tenancy;

/// <summary>
/// Tenant endpoints serve only <see cref="TenantStatus.Active"/> tenants: none → 400 <c>AUX-11008</c>, provisioning or
/// failed migration → 503 <c>AUX-11010</c>, suspended → 423 <c>AUX-11011</c>, archived → 404 <c>AUX-11009</c>.
/// Platform endpoints (System console) do not use this filter: they operate on tenants in any status.
/// </summary>
internal sealed class TenantEndpointFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var tenant = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>().Current;
        IResult? problem = tenant?.Status switch
        {
            null => Errors.Tenancy.TenantRequired().ToProblem(),
            TenantStatus.Active => null,
            TenantStatus.Suspended => Problem(EventCodes.Tenancy.TenantSuspended, StatusCodes.Status423Locked, "The tenant is suspended"),
            TenantStatus.Archived => Errors.Tenancy.TenantNotFound().ToProblem(),
            _ => Problem(EventCodes.Tenancy.TenantUnavailable, StatusCodes.Status503ServiceUnavailable, "The tenant is not available"),
        };

        return problem is null ? next(context) : ValueTask.FromResult<object?>(problem);
    }

    private static Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult Problem(int code, int status, string title) =>
        TypedResults.Problem(
            title: title,
            statusCode: status,
            type: ProblemDetailsSetup.ErrorTypeUri(code),
            extensions: new Dictionary<string, object?> { [ProblemDetailsSetup.ErrorCodeKey] = ProblemDetailsSetup.ErrorCode(code) });
}

public static class TenantEndpointExtensions
{
    /// <summary>The endpoints need an active tenant (see <see cref="TenantEndpointFilter"/>).</summary>
    public static TBuilder RequireTenant<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter<TBuilder, TenantEndpointFilter>();
}
