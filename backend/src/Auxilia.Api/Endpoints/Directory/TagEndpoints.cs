using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Directory;
using Auxilia.Contracts.Directory;

namespace Auxilia.Api.Endpoints.Directory;

/// <summary>
/// Tags and consents of the clients (N01, M-01): Administrators manage the tags; staff put them on clients (one or in
/// bulk) and record consents. Module <c>directory</c>: 404 when not visible to the role.
/// </summary>
internal sealed class TagEndpoints : IModuleEndpoints
{
    public string ModuleCode => DirectoryModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var tags = module.MapGroup("/tags").WithTags("Clients");

        tags.MapGet("/", async (ITagQueryService query, CancellationToken cancellationToken) => TypedResults.Ok(await query.ListAsync(cancellationToken)))
            .RequirePermission(DirectoryPermissions.ViewClients)
            .WithName("ListTags")
            .WithSummary("The tags of the clients, by name, with the number of clients that have each")
            .Produces<IReadOnlyList<TagResponse>>();

        tags.MapPost("/", CreateAsync)
            .RequirePermission(DirectoryPermissions.ManageTags)
            .WithName("CreateTag")
            .WithSummary("Creates a tag (unique name, optional colour #rrggbb)")
            .Produces<CreateTagResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        tags.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(DirectoryPermissions.ManageTags)
            .WithName("UpdateTag")
            .WithSummary("Renames or recolours a tag")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        tags.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(DirectoryPermissions.ManageTags)
            .WithName("DeleteTag")
            .WithSummary("Deletes a tag and takes it off every client")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var clients = module.MapGroup("/clients").WithTags("Clients");

        clients.MapPost("/tags", BulkAsync)
            .RequirePermission(DirectoryPermissions.ManageClients)
            .WithName("ChangeClientsTags")
            .WithSummary("Adds and removes tags on many clients at once (at most 500)")
            .Produces<BulkClientTagsResponse>()
            .ProducesValidationProblem();

        clients.MapGet("/{id:guid}/tags", ClientTagsAsync)
            .RequirePermission(DirectoryPermissions.ViewClients)
            .WithName("GetClientTags")
            .WithSummary("The tags of a client")
            .Produces<IReadOnlyList<ClientTagResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        clients.MapPut("/{id:guid}/tags", SetClientTagsAsync)
            .RequirePermission(DirectoryPermissions.ManageClients)
            .WithName("SetClientTags")
            .WithSummary("Replaces the tags of a client")
            .Produces<IReadOnlyList<ClientTagResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        clients.MapGet("/{id:guid}/consents", ConsentsAsync)
            .RequirePermission(DirectoryPermissions.ViewClients)
            .WithName("GetClientConsents")
            .WithSummary("The consents of a client: current state per purpose and channel and every change")
            .Produces<ClientConsentsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        clients.MapPost("/{id:guid}/consents", RecordConsentAsync)
            .RequirePermission(DirectoryPermissions.ManageClients)
            .WithName("RecordClientConsent")
            .WithSummary("Grants or revokes a consent of a client (source Staff); answers with the consents")
            .Produces<ClientConsentsResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> CreateAsync(SaveTagRequest request, ITagManager manager, CancellationToken cancellationToken) =>
        (await manager.CreateAsync(request, cancellationToken)).ToHttpResult(id => TypedResults.Created($"/api/v1/tags/{id}", new CreateTagResponse(id)));

    private static async Task<IResult> UpdateAsync(Guid id, SaveTagRequest request, ITagManager manager, CancellationToken cancellationToken) =>
        (await manager.UpdateAsync(id, request, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> DeleteAsync(Guid id, ITagManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> BulkAsync(BulkClientTagsRequest request, ITagManager manager, CancellationToken cancellationToken) =>
        (await manager.BulkAsync(request, cancellationToken)).ToHttpResult(changed => TypedResults.Ok(new BulkClientTagsResponse(changed)));

    private static async Task<IResult> ClientTagsAsync(Guid id, ITagQueryService query, CancellationToken cancellationToken) =>
        (await query.ClientTagsAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> SetClientTagsAsync(Guid id, SetClientTagsRequest request, ITagManager manager, ITagQueryService query, CancellationToken cancellationToken)
    {
        var set = await manager.SetClientTagsAsync(id, request.TagIds, cancellationToken);
        return set.IsFailure ? set.Error!.ToProblem() : (await query.ClientTagsAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);
    }

    private static async Task<IResult> ConsentsAsync(Guid id, IConsentQueryService query, CancellationToken cancellationToken) =>
        (await query.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> RecordConsentAsync(
        Guid id, RecordConsentRequest request, IConsentManager manager, IConsentQueryService query, CancellationToken cancellationToken)
    {
        var recorded = await manager.RecordAsync(id, request, cancellationToken);
        return recorded.IsFailure ? recorded.Error!.ToProblem() : (await query.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);
    }
}
