using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Marketing;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Marketing;

namespace Auxilia.Api.Endpoints.Marketing;

/// <summary>
/// Audiences of the campaigns (N01, M-02): dynamic segments (validated rule, live count and preview) and static lists.
/// Employees count and see only the clients in their charge. Module <c>marketing</c>: 404 when not visible to the role.
/// </summary>
internal sealed class AudienceEndpoints : IModuleEndpoints
{
    public string ModuleCode => MarketingModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var segments = module.MapGroup("/marketing/segments").WithTags("Marketing");

        segments.MapGet("/fields", (ISegmentQueryService query) => TypedResults.Ok(query.Fields()))
            .RequirePermission(MarketingPermissions.ViewAudiences)
            .WithName("ListSegmentFields")
            .WithSummary("The fields a segment rule can use with their operators")
            .Produces<IReadOnlyList<SegmentFieldResponse>>();

        segments.MapGet("/", async (ISegmentQueryService query, CancellationToken cancellationToken) => TypedResults.Ok(await query.ListAsync(cancellationToken)))
            .RequirePermission(MarketingPermissions.ViewAudiences)
            .WithName("ListSegments")
            .WithSummary("The segments of the tenant, by name")
            .Produces<IReadOnlyList<SegmentListItemResponse>>();

        segments.MapPost("/preview", PreviewAsync)
            .RequirePermission(MarketingPermissions.ViewAudiences)
            .WithName("PreviewSegment")
            .WithSummary("How many clients a rule selects now and the first ones by name")
            .Produces<SegmentPreviewResponse>()
            .ProducesValidationProblem();

        segments.MapPost("/", CreateSegmentAsync)
            .RequirePermission(MarketingPermissions.ManageAudiences)
            .WithName("CreateSegment")
            .WithSummary("Saves a segment with its rule (unique name)")
            .Produces<CreatedAudienceResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        segments.MapGet("/{id:guid}", GetSegmentAsync)
            .WithETag()
            .RequirePermission(MarketingPermissions.ViewAudiences)
            .WithName("GetSegment")
            .WithSummary("A segment with its rule and how many clients it selects now")
            .Produces<SegmentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        segments.MapPut("/{id:guid}", UpdateSegmentAsync)
            .RequireIfMatch()
            .RequirePermission(MarketingPermissions.ManageAudiences)
            .WithName("UpdateSegment")
            .WithSummary("Changes the name, description or rule of a segment")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        segments.MapDelete("/{id:guid}", DeleteSegmentAsync)
            .RequireIfMatch()
            .RequirePermission(MarketingPermissions.ManageAudiences)
            .WithName("DeleteSegment")
            .WithSummary("Deletes a segment")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        segments.MapGet("/{id:guid}/members", SegmentMembersAsync)
            .RequirePermission(MarketingPermissions.ViewAudiences)
            .WithName("ListSegmentMembers")
            .WithSummary("The clients a segment selects now, by name")
            .Produces<PagedResponse<AudienceMemberResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        var lists = module.MapGroup("/marketing/lists").WithTags("Marketing");

        lists.MapGet("/", async (IStaticListQueryService query, CancellationToken cancellationToken) => TypedResults.Ok(await query.ListAsync(cancellationToken)))
            .RequirePermission(MarketingPermissions.ViewAudiences)
            .WithName("ListStaticLists")
            .WithSummary("The static lists of the tenant, by name, with their members")
            .Produces<IReadOnlyList<StaticListResponse>>();

        lists.MapPost("/", CreateListAsync)
            .RequirePermission(MarketingPermissions.ManageAudiences)
            .WithName("CreateStaticList")
            .WithSummary("Creates a static list (unique name)")
            .Produces<CreatedAudienceResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        lists.MapGet("/{id:guid}", GetListAsync)
            .WithETag()
            .RequirePermission(MarketingPermissions.ViewAudiences)
            .WithName("GetStaticList")
            .WithSummary("A static list")
            .Produces<StaticListResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        lists.MapPut("/{id:guid}", UpdateListAsync)
            .RequireIfMatch()
            .RequirePermission(MarketingPermissions.ManageAudiences)
            .WithName("UpdateStaticList")
            .WithSummary("Renames a static list")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        lists.MapDelete("/{id:guid}", DeleteListAsync)
            .RequireIfMatch()
            .RequirePermission(MarketingPermissions.ManageAudiences)
            .WithName("DeleteStaticList")
            .WithSummary("Deletes a static list with its members")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        lists.MapGet("/{id:guid}/members", ListMembersAsync)
            .RequirePermission(MarketingPermissions.ViewAudiences)
            .WithName("ListStaticListMembers")
            .WithSummary("The clients of a static list, by name")
            .Produces<PagedResponse<AudienceMemberResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        lists.MapPost("/{id:guid}/members", AddMembersAsync)
            .RequirePermission(MarketingPermissions.ManageAudiences)
            .WithName("AddStaticListMembers")
            .WithSummary("Adds clients (e.g. a selection of the clients table, at most 500)")
            .Produces<ListMembersChangedResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        lists.MapPost("/{id:guid}/members/remove", RemoveMembersAsync)
            .RequirePermission(MarketingPermissions.ManageAudiences)
            .WithName("RemoveStaticListMembers")
            .WithSummary("Removes clients from a static list")
            .Produces<ListMembersChangedResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> PreviewAsync(SegmentRuleRequest rule, ISegmentQueryService query, CancellationToken cancellationToken) =>
        (await query.PreviewAsync(rule, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> CreateSegmentAsync(SaveSegmentRequest request, ISegmentManager manager, CancellationToken cancellationToken) =>
        (await manager.CreateAsync(request, cancellationToken)).ToHttpResult(id => TypedResults.Created($"/api/v1/marketing/segments/{id}", new CreatedAudienceResponse(id)));

    private static async Task<IResult> GetSegmentAsync(Guid id, ISegmentQueryService query, CancellationToken cancellationToken) =>
        (await query.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> UpdateSegmentAsync(Guid id, SaveSegmentRequest request, ISegmentManager manager, CancellationToken cancellationToken) =>
        (await manager.UpdateAsync(id, request, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> DeleteSegmentAsync(Guid id, ISegmentManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> SegmentMembersAsync(Guid id, ISegmentQueryService query, int? page, int? pageSize, CancellationToken cancellationToken) =>
        (await query.MembersAsync(id, page ?? 1, pageSize ?? 25, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> CreateListAsync(SaveStaticListRequest request, IStaticListManager manager, CancellationToken cancellationToken) =>
        (await manager.CreateAsync(request, cancellationToken)).ToHttpResult(id => TypedResults.Created($"/api/v1/marketing/lists/{id}", new CreatedAudienceResponse(id)));

    private static async Task<IResult> GetListAsync(Guid id, IStaticListQueryService query, CancellationToken cancellationToken) =>
        (await query.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> UpdateListAsync(Guid id, SaveStaticListRequest request, IStaticListManager manager, CancellationToken cancellationToken) =>
        (await manager.UpdateAsync(id, request, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> DeleteListAsync(Guid id, IStaticListManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> ListMembersAsync(Guid id, IStaticListQueryService query, int? page, int? pageSize, CancellationToken cancellationToken) =>
        (await query.MembersAsync(id, page ?? 1, pageSize ?? 25, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> AddMembersAsync(Guid id, ListMembersRequest request, IStaticListManager manager, CancellationToken cancellationToken) =>
        (await manager.AddMembersAsync(id, request.ClientIds, cancellationToken)).ToHttpResult(changed => TypedResults.Ok(new ListMembersChangedResponse(changed)));

    private static async Task<IResult> RemoveMembersAsync(Guid id, ListMembersRequest request, IStaticListManager manager, CancellationToken cancellationToken) =>
        (await manager.RemoveMembersAsync(id, request.ClientIds, cancellationToken)).ToHttpResult(changed => TypedResults.Ok(new ListMembersChangedResponse(changed)));
}
