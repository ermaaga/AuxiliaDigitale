using Auxilia.Api.Endpoints;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.Tenancy;
using Auxilia.Application.Abstractions.Modules;

namespace Auxilia.Api.Modules;

/// <summary>
/// A module endpoint exists only where the module is visible (ARCHITECTURE §5.2): otherwise 404 with the same body as an
/// unknown route (<c>AUX-10017</c>), so a hidden module is indistinguishable from a missing one.
/// </summary>
internal sealed class ModuleEndpointFilter(string moduleCode) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var access = context.HttpContext.RequestServices.GetRequiredService<IModuleAccess>();
        return await access.IsVisibleAsync(moduleCode, context.HttpContext.RequestAborted)
            ? await next(context)
            : TypedResults.NotFound();
    }
}

public static class ModuleEndpointExtensions
{
    /// <summary>
    /// Maps every <see cref="IModuleEndpoints"/> on <paramref name="api"/>: active tenant first (tenant status errors),
    /// then module visibility. Fails at startup when an endpoint set has no module descriptor.
    /// </summary>
    public static void MapModules(this RouteGroupBuilder api, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(services);

        var registry = services.GetRequiredService<IModuleRegistry>();
        foreach (var endpoints in services.GetServices<IModuleEndpoints>())
        {
            if (registry.Find(endpoints.ModuleCode) is null)
            {
                throw new InvalidOperationException($"Endpoints of module '{endpoints.ModuleCode}' have no module descriptor.");
            }

            var group = api.MapGroup(string.Empty)
                .RequireTenant()
                .AddEndpointFilter(new ModuleEndpointFilter(endpoints.ModuleCode))
                .RejectUnknownFilters()
                .WithMetadata(new ModuleMetadata(endpoints.ModuleCode));
            endpoints.Map(group);
        }
    }
}

/// <summary>Endpoint metadata: the module an endpoint belongs to.</summary>
public sealed record ModuleMetadata(string ModuleCode);
