using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Engagement;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Engagement;
using Auxilia.SharedKernel.Results;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Engagement;

/// <summary>
/// Requests (F15): inbox (received / sent / all), thread, new request, reply, close, delete. A request the caller
/// cannot see is 404. Module <c>engagement</c>: 404 when not visible to the role.
/// </summary>
internal sealed class RequestEndpoints : IModuleEndpoints
{
    public string ModuleCode => EngagementModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var requests = module.MapGroup("/requests").WithTags("Requests");

        requests.MapGet("/", ListAsync)
            .RequirePermission(EngagementPermissions.ViewRequests)
            .WithName("ListRequests")
            .WithSummary("The inbox: requests received (employee: addressed to me; Administrators: the office), sent, or all (Administrators)")
            .Produces<PagedResponse<RequestListItemResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        requests.MapGet("/{id:guid}", GetAsync)
            .WithETag()
            .RequirePermission(EngagementPermissions.ViewRequests)
            .WithName("GetRequest")
            .WithSummary("A request with its thread and what the caller may do")
            .Produces<RequestResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        requests.MapPost("/", CreateAsync)
            .RequirePermission(EngagementPermissions.ManageRequests)
            .WithName("CreateRequest")
            .WithSummary("Clients ask the employee in charge (default) or the office; employees ask the office")
            .Produces<RequestResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        requests.MapPost("/{id:guid}/messages", ReplyAsync)
            .RequirePermission(EngagementPermissions.ManageRequests)
            .WithName("ReplyToRequest")
            .WithSummary("Adds a message to an open request; the other party is told")
            .Produces<RequestResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        requests.MapPost("/{id:guid}/close", CloseAsync)
            .RequirePermission(EngagementPermissions.ManageRequests)
            .WithName("CloseRequest")
            .WithSummary("Closes an open request")
            .Produces<RequestResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        requests.MapDelete("/{id:guid}", DeleteAsync)
            .RequireIfMatch()
            .RequirePermission(EngagementPermissions.DeleteRequests)
            .WithName("DeleteRequest")
            .WithSummary("Administrators delete a request (soft delete, thread kept)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(
        IRequestQueryService requests,
        string? box,
        [FromQuery(Name = "filter[status]")] string? status,
        [FromQuery(Name = "filter[type]")] string? type,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await requests.ListAsync(new RequestListQuery(box, status, type, sort, page ?? 1, pageSize ?? 25), cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetAsync(Guid id, IRequestQueryService requests, CancellationToken cancellationToken) =>
        (await requests.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> CreateAsync(CreateRequestRequest request, IRequestManager manager, IRequestQueryService requests, CancellationToken cancellationToken)
    {
        var created = await manager.CreateAsync(request, cancellationToken);
        return created.IsFailure
            ? created.Error!.ToProblem()
            : (await requests.GetAsync(created.Value, cancellationToken)).ToHttpResult(detail => TypedResults.Created($"/api/v1/requests/{detail.Id}", detail));
    }

    private static async Task<IResult> ReplyAsync(
        Guid id, ReplyToRequestRequest request, IRequestManager manager, IRequestQueryService requests, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.ReplyAsync(id, request, cancellationToken), id, requests, cancellationToken);

    private static async Task<IResult> CloseAsync(Guid id, IRequestManager manager, IRequestQueryService requests, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.CloseAsync(id, cancellationToken), id, requests, cancellationToken);

    private static async Task<IResult> DeleteAsync(Guid id, IRequestManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    /// <summary>A change answers with the request as it is now.</summary>
    private static async Task<IResult> DetailAfterAsync(Result change, Guid id, IRequestQueryService requests, CancellationToken cancellationToken) =>
        change.IsFailure ? change.Error!.ToProblem() : (await requests.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);
}
