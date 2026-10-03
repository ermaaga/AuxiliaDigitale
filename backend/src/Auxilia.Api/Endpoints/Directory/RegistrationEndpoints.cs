using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Directory;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Directory;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Directory;

/// <summary>
/// Registration requests for staff (F03, API only D-14/D-27): one list with filters for Administrators and Employees,
/// detail, approve (creates the client) and reject, once. Module <c>directory</c>: 404 when not visible to the
/// caller's role.
/// </summary>
internal sealed class RegistrationEndpoints : IModuleEndpoints
{
    public string ModuleCode => DirectoryModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var registrations = module.MapGroup("/registrations").WithTags("Registrations");

        registrations.MapGet("/", ListAsync)
            .RequirePermission(DirectoryPermissions.ReviewRegistrations)
            .WithName("ListRegistrations")
            .WithSummary("Registration requests filtered by status, request date and text (name, surname, e-mail, fiscal code)")
            .Produces<PagedResponse<RegistrationResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        registrations.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(DirectoryPermissions.ReviewRegistrations)
            .WithName("GetRegistration")
            .WithSummary("A registration request")
            .Produces<RegistrationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        registrations.MapPost("/{id:guid}/approve", ApproveAsync)
            .RequirePermission(DirectoryPermissions.ReviewRegistrations)
            .WithName("ApproveRegistration")
            .WithSummary("Approves a pending request: creates the client (user name = e-mail), assigned to the default employee, and sends the activation e-mail")
            .Produces<ApproveRegistrationResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        registrations.MapPost("/{id:guid}/reject", RejectAsync)
            .RequirePermission(DirectoryPermissions.ReviewRegistrations)
            .WithName("RejectRegistration")
            .WithSummary("Rejects a pending request with optional notes; no client is created")
            .Produces<RegistrationResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> ListAsync(
        IRegistrationQueryService registrations,
        [FromQuery(Name = "filter[status]")] string? status,
        [FromQuery(Name = "filter[from]")] DateOnly? from,
        [FromQuery(Name = "filter[to]")] DateOnly? to,
        string? search,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await registrations.ListAsync(new RegistrationListQuery(status, from, to, search, sort, page ?? 1, pageSize ?? 25), cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetAsync(Guid id, IRegistrationQueryService registrations, CancellationToken cancellationToken) =>
        (await registrations.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ApproveAsync(
        Guid id, ProcessRegistrationRequest? request, IRegistrationManager manager, CancellationToken cancellationToken) =>
        (await manager.ApproveAsync(id, request?.Notes, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> RejectAsync(
        Guid id, ProcessRegistrationRequest? request, IRegistrationManager manager, IRegistrationQueryService registrations, CancellationToken cancellationToken)
    {
        var rejected = await manager.RejectAsync(id, request?.Notes, cancellationToken);
        return rejected.IsFailure ? rejected.Error!.ToProblem() : (await registrations.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);
    }
}
