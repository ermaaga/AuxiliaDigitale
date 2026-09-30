using Auxilia.Api.Infrastructure;
using Auxilia.Api.Tenancy;
using Auxilia.Application.Identity;
using Auxilia.Application.Platform.Modules;
using Auxilia.Contracts.Identity;
using Auxilia.Contracts.Platform;

namespace Auxilia.Api.Endpoints.Identity;

/// <summary>
/// The signed-in tenant user (F22): <c>GET /me</c> (profile, roles, effective permissions) and <c>GET /me/navigation</c>
/// (menu of the visible modules the user may open). Any authenticated tenant user: no extra permission.
/// </summary>
internal sealed class MeEndpoints : IApiEndpoints
{
    public void Map(RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Me").RequireTenant().RequireAuthorization();

        me.MapGet(string.Empty, GetMeAsync)
            .WithName("GetMe")
            .WithSummary("The signed-in user, the tenant, every role and the effective permissions")
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("/navigation", GetNavigationAsync)
            .WithName("GetMyNavigation")
            .WithSummary("Menu of the tenant app for the signed-in user (visible modules, roles and permissions)")
            .Produces<IReadOnlyList<NavigationItemResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> GetMeAsync(ICurrentUserQueryService users, CancellationToken cancellationToken) =>
        (await users.GetAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetNavigationAsync(INavigationQueryService navigation, CancellationToken cancellationToken) =>
        TypedResults.Ok(await navigation.GetAsync(cancellationToken));
}
