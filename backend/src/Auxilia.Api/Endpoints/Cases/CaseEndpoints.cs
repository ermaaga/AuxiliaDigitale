using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Cases;
using Auxilia.Contracts.Cases;
using Auxilia.Contracts.Common;
using Auxilia.SharedKernel.Results;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Cases;

/// <summary>
/// Cases (F09): open, detail with timeline and payments, one status forward or back, complete, payments, due date and
/// custom fields, soft delete. Visibility and management follow F10 (D-04): a case the caller cannot see is 404 (Q10),
/// one it sees but may not change is 403; the lists apply the same rules in the query (B-09). Module <c>cases</c>: 404 when not visible to the role.
/// </summary>
internal sealed class CaseEndpoints : IModuleEndpoints
{
    public string ModuleCode => CasesModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var cases = module.MapGroup("/cases").WithTags("Cases");

        cases.MapGet("/", ListAsync)
            .RequirePermission(CasesPermissions.ViewCases)
            .WithName("ListCases")
            .WithSummary("The cases the caller may see (F10), filtered by client, service and status, with the show all / show completed toggles")
            .Produces<PagedResponse<CaseListItemResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        cases.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(CasesPermissions.ViewCases)
            .WithName("GetCase")
            .WithSummary("A case with client, service, timeline, payments and what the caller may do")
            .Produces<CaseResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        cases.MapPost("/", OpenAsync)
            .RequirePermission(CasesPermissions.ManageCases)
            .WithName("OpenCase")
            .WithSummary("Opens a case for a client and an active service (price snapshot, number {year}-{sequence})")
            .Produces<CaseResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        cases.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(CasesPermissions.ManageCases)
            .WithName("UpdateCase")
            .WithSummary("Changes the due date and the custom fields of a case not completed")
            .Produces<CaseResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        cases.MapPost("/{id:guid}/advance", AdvanceAsync)
            .RequirePermission(CasesPermissions.ManageCases)
            .WithName("AdvanceCase")
            .WithSummary("Moves a case one status forward (Inserted → InProgress → Sent); a sent case is completed instead")
            .Produces<CaseResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        cases.MapPost("/{id:guid}/back", GoBackAsync)
            .RequirePermission(CasesPermissions.ManageCases)
            .WithName("MoveCaseBack")
            .WithSummary("Moves a case one status back (from InProgress or Sent)")
            .Produces<CaseResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        cases.MapPost("/{id:guid}/complete", CompleteAsync)
            .RequirePermission(CasesPermissions.ManageCases)
            .WithName("CompleteCase")
            .WithSummary("Completes a sent case with the amount received and the outcome; then it never changes")
            .Produces<CaseResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        cases.MapPost("/{id:guid}/payments", AddPaymentAsync)
            .RequirePermission(CasesPermissions.ManageCases)
            .WithName("AddCasePayment")
            .WithSummary("Records money received for a case not completed")
            .Produces<CaseResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        cases.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(CasesPermissions.DeleteCases)
            .WithName("DeleteCase")
            .WithSummary("Deletes a case (soft delete) and recomputes the client status")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(
        ICaseQueryService cases,
        [FromQuery(Name = "filter[clientName]")] string? clientName,
        [FromQuery(Name = "filter[serviceName]")] string? serviceName,
        [FromQuery(Name = "filter[clientId]")] Guid? clientId,
        [FromQuery(Name = "filter[serviceId]")] Guid? serviceId,
        [FromQuery(Name = "filter[status]")] string? status,
        bool? showAll,
        bool? showCompleted,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await cases.ListAsync(
            new CaseListQuery(clientName, serviceName, clientId, serviceId, status, showAll, showCompleted, sort, page ?? 1, pageSize ?? 25), cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetAsync(Guid id, ICaseQueryService cases, CancellationToken cancellationToken) =>
        (await cases.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> OpenAsync(OpenCaseRequest request, ICaseManager manager, ICaseQueryService cases, CancellationToken cancellationToken)
    {
        var opened = await manager.OpenAsync(request, cancellationToken);
        return opened.IsFailure
            ? opened.Error!.ToProblem()
            : (await cases.GetAsync(opened.Value, cancellationToken)).ToHttpResult(detail => TypedResults.Created($"/api/v1/cases/{detail.Id}", detail));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateCaseRequest request, ICaseManager manager, ICaseQueryService cases, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.UpdateAsync(id, request, cancellationToken), id, cases, cancellationToken);

    private static async Task<IResult> AdvanceAsync(
        Guid id, ChangeCaseStatusRequest? request, ICaseManager manager, ICaseQueryService cases, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.AdvanceAsync(id, request?.Note, cancellationToken), id, cases, cancellationToken);

    private static async Task<IResult> GoBackAsync(
        Guid id, ChangeCaseStatusRequest? request, ICaseManager manager, ICaseQueryService cases, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.GoBackAsync(id, request?.Note, cancellationToken), id, cases, cancellationToken);

    private static async Task<IResult> CompleteAsync(Guid id, CompleteCaseRequest request, ICaseManager manager, ICaseQueryService cases, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.CompleteAsync(id, request, cancellationToken), id, cases, cancellationToken);

    private static async Task<IResult> AddPaymentAsync(Guid id, AddCasePaymentRequest request, ICaseManager manager, ICaseQueryService cases, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.AddPaymentAsync(id, request, cancellationToken), id, cases, cancellationToken);

    private static async Task<IResult> DeleteAsync(Guid id, ICaseManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    /// <summary>A change answers with the case as it is now.</summary>
    private static async Task<IResult> DetailAfterAsync(Result change, Guid id, ICaseQueryService cases, CancellationToken cancellationToken) =>
        change.IsFailure ? change.Error!.ToProblem() : (await cases.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);
}
