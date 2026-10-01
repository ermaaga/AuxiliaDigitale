using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Identity;
using Auxilia.Application.Identity.Public;
using Auxilia.Contracts.Platform;

namespace Auxilia.Api.Endpoints.Platform;

/// <summary>
/// Technical endpoints of one tenant for the System (N02, tenant-scoped platform token): its Administrator accounts,
/// the first Administrator and the invitation e-mail. The activation link only ever leaves by e-mail (D-21).
/// </summary>
internal sealed class AdministratorEndpoints : IApiEndpoints
{
    public void Map(RouteGroupBuilder api)
    {
        var administrators = api.MapGroup("/administrators").WithTags("Platform").RequirePlatformTenant();

        administrators.MapGet("/", async (ITenantAdministratorManager manager, CancellationToken cancellationToken) =>
                TypedResults.Ok(await manager.ListAsync(cancellationToken)))
            .WithName("ListTenantAdministrators")
            .WithSummary("Administrator accounts of the tenant and whether they are activated")
            .Produces<IReadOnlyList<TenantAdministratorResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        administrators.MapPost("/", async (CreateTenantAdministratorRequest request, ITenantAdministratorManager manager, CancellationToken cancellationToken) =>
                (await manager.CreateInitialAsync(request, cancellationToken)).ToHttpResult(created =>
                    TypedResults.Created($"/api/v1/administrators/{created.UserId}", created)))
            .WithName("CreateTenantAdministrator")
            .WithSummary("Creates the first Administrator of the tenant and e-mails the invitation (pending without a sending account)")
            .Produces<TenantAdministratorInvitationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        administrators.MapPost("/{userId:guid}/invitation", async (Guid userId, ITenantAdministratorManager manager, CancellationToken cancellationToken) =>
                (await manager.SendInvitationAsync(userId, cancellationToken)).ToHttpResult(TypedResults.Ok))
            .WithName("SendTenantAdministratorInvitation")
            .WithSummary("E-mails a new activation link to an Administrator who has not activated the account")
            .Produces<TenantAdministratorInvitationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }
}
