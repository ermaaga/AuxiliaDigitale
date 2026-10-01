using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Directory;
using Auxilia.Contracts.Directory;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Directory;

/// <summary>
/// Role specializations (F12, D-18) and the users holding them: managed by the System with a platform token scoped to
/// the tenant (D-21). Deleting deactivates (legacy behaviour).
/// </summary>
internal sealed class SpecializationEndpoints : IApiEndpoints
{
    public void Map(RouteGroupBuilder api)
    {
        var specializations = api.MapGroup("/specializations").WithTags("Directory").RequirePlatformTenant();

        specializations.MapGet(string.Empty, ListAsync)
            .WithName("ListSpecializations")
            .WithSummary("Active specializations, optionally of one role, by role then name")
            .Produces<IReadOnlyList<SpecializationResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        specializations.MapPost(string.Empty, CreateAsync)
            .WithName("CreateSpecialization")
            .WithSummary("Adds a specialization of the Client or Employee role")
            .Produces<CreateSpecializationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        specializations.MapPut("/{id:guid}", UpdateAsync)
            .WithName("UpdateSpecialization")
            .WithSummary("Changes a specialization (the role stays)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        specializations.MapDelete("/{id:guid}", DeactivateAsync)
            .WithName("DeactivateSpecialization")
            .WithSummary("Deactivates a specialization (it keeps its members)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        specializations.MapGet("/{id:guid}/members", ListMembersAsync)
            .WithName("ListSpecializationMembers")
            .WithSummary("Users holding the specialization, by user name")
            .Produces<IReadOnlyList<SpecializationMemberResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        specializations.MapGet("/{id:guid}/candidates", ListCandidatesAsync)
            .WithName("ListSpecializationCandidates")
            .WithSummary("Users of the specialization's role who do not hold it (at most 50), matching the search")
            .Produces<IReadOnlyList<SpecializationMemberResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        specializations.MapPost("/{id:guid}/members", AddMembersAsync)
            .WithName("AddSpecializationMembers")
            .WithSummary("Gives the specialization to users of its role")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        specializations.MapDelete("/{id:guid}/members/{userId:guid}", RemoveMemberAsync)
            .WithName("RemoveSpecializationMember")
            .WithSummary("Takes the specialization away from a user")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(
        ISpecializationQueryService query, [FromQuery(Name = "filter[role]")] string? role, CancellationToken cancellationToken) =>
        (await query.ListAsync(role, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> CreateAsync(CreateSpecializationRequest request, ISpecializationManager manager, CancellationToken cancellationToken) =>
        (await manager.CreateAsync(request, cancellationToken))
            .ToHttpResult(id => TypedResults.Created($"/api/v1/specializations/{id}", new CreateSpecializationResponse(id)));

    private static async Task<IResult> UpdateAsync(Guid id, UpdateSpecializationRequest request, ISpecializationManager manager, CancellationToken cancellationToken) =>
        (await manager.UpdateAsync(id, request, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> DeactivateAsync(Guid id, ISpecializationManager manager, CancellationToken cancellationToken) =>
        (await manager.DeactivateAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> ListMembersAsync(Guid id, ISpecializationQueryService query, CancellationToken cancellationToken) =>
        (await query.MembersAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ListCandidatesAsync(
        Guid id, ISpecializationQueryService query, [FromQuery] string? search, CancellationToken cancellationToken) =>
        (await query.CandidatesAsync(id, search, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> AddMembersAsync(Guid id, AddSpecializationMembersRequest request, ISpecializationManager manager, CancellationToken cancellationToken) =>
        (await manager.AddMembersAsync(id, request.UserIds, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> RemoveMemberAsync(Guid id, Guid userId, ISpecializationManager manager, CancellationToken cancellationToken) =>
        (await manager.RemoveMemberAsync(id, userId, cancellationToken)).ToHttpResult(TypedResults.NoContent);
}
