using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Identity;
using Auxilia.Contracts.Identity;

namespace Auxilia.Api.Endpoints.Identity;

/// <summary>
/// Permissions of the tenant roles (F22, D-18): edited by the System with a platform token scoped to the tenant (D-21).
/// A change applies to the next request of every user of the role.
/// </summary>
internal sealed class RolePermissionEndpoints : IApiEndpoints
{
    public void Map(RouteGroupBuilder api)
    {
        var permissions = api.MapGroup("/role-permissions").WithTags("Identity").RequirePlatformTenant();

        permissions.MapGet(string.Empty, ListAsync)
            .WithName("ListRolePermissions")
            .WithSummary("Permissions declared by the modules with the roles holding them and their default roles")
            .Produces<IReadOnlyList<RolePermissionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        permissions.MapPut("/{role}", SetAsync)
            .WithName("SetRolePermissions")
            .WithSummary("Sets every permission a role holds")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        permissions.MapDelete("/{role}", ResetAsync)
            .WithName("ResetRolePermissions")
            .WithSummary("Gives a role the default permissions of the modules again")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ListAsync(IRolePermissionQueryService query, CancellationToken cancellationToken) =>
        TypedResults.Ok(await query.ListAsync(cancellationToken));

    private static async Task<IResult> SetAsync(string role, SetRolePermissionsRequest request, IRolePermissionManager manager, CancellationToken cancellationToken) =>
        (await manager.SetAsync(role, request.Permissions, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> ResetAsync(string role, IRolePermissionManager manager, CancellationToken cancellationToken) =>
        (await manager.ResetAsync(role, cancellationToken)).ToHttpResult(TypedResults.NoContent);
}
