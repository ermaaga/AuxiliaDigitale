using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Directory;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Directory;
using Auxilia.SharedKernel.Results;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Directory;

/// <summary>
/// Clients of the tenant for staff (F05): lists, detail, create, edit, soft delete, sign-in, employee in charge,
/// specializations, invitation and password reset. Module <c>directory</c>: 404 when not visible to the caller's role.
/// </summary>
internal sealed class ClientEndpoints : IModuleEndpoints
{
    public string ModuleCode => DirectoryModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var clients = module.MapGroup("/clients").WithTags("Clients");

        clients.MapGet("/", ListAsync)
            .RequirePermission(DirectoryPermissions.ViewClients)
            .WithName("ListClients")
            .WithSummary("Clients, all or mine (view=mine), filtered by name, surname, e-mail, user name, phone, status and employee")
            .Produces<PagedResponse<ClientListItemResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        clients.MapGet("/assignable-employees", AssignableEmployeesAsync)
            .RequirePermission(DirectoryPermissions.ViewClients)
            .WithName("ListAssignableEmployees")
            .WithSummary("Employees a client can be assigned to (Employee role, can sign in)")
            .Produces<IReadOnlyList<ClientEmployeeResponse>>();

        clients.MapGet("/specializations", SpecializationsAsync)
            .RequirePermission(DirectoryPermissions.ViewClients)
            .WithName("ListClientSpecializations")
            .WithSummary("Active specializations of the Client role a client can be given")
            .Produces<IReadOnlyList<ClientSpecializationResponse>>();

        clients.MapGet("/{id:guid}", GetAsync)
            .WithETag()
            .RequirePermission(DirectoryPermissions.ViewClients)
            .WithName("GetClient")
            .WithSummary("A client: personal data, account, employee in charge with history, specializations")
            .Produces<ClientDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        clients.MapPost("/", CreateAsync)
            .RequirePermission(DirectoryPermissions.ManageClients)
            .WithName("CreateClient")
            .WithSummary("Creates a client (e-mail = user name); by an Administrator it can sign in and gets the activation e-mail")
            .Produces<CreateClientResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        clients.MapPut("/{id:guid}", UpdateAsync)
            .RequireIfMatch()
            .RequirePermission(DirectoryPermissions.ManageClients)
            .WithName("UpdateClient")
            .WithSummary("Changes personal data, user name and custom fields of a client")
            .Produces<ClientDetailResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        clients.MapDelete("/{id:guid}", DeleteAsync)
            .RequireIfMatch()
            .RequirePermission(DirectoryPermissions.DeleteClients)
            .WithName("DeleteClient")
            .WithSummary("Deletes a client (soft delete): hidden from the lists, sign-in disabled")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        clients.MapPut("/{id:guid}/sign-in", SetSignInAsync)
            .RequirePermission(DirectoryPermissions.ManageClients)
            .WithName("SetClientSignIn")
            .WithSummary("Enables or disables the client's sign-in; enabling needs an employee in charge")
            .Produces<ClientDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        clients.MapPut("/{id:guid}/employee", AssignAsync)
            .RequirePermission(DirectoryPermissions.AssignClients)
            .WithName("AssignClientEmployee")
            .WithSummary("Hands the client to an employee (the previous assignment ends, history kept)")
            .Produces<ClientDetailResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        clients.MapDelete("/{id:guid}/employee", UnassignAsync)
            .RequirePermission(DirectoryPermissions.AssignClients)
            .WithName("UnassignClientEmployee")
            .WithSummary("Nobody in charge of the client from now on")
            .Produces<ClientDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        clients.MapPut("/{id:guid}/specializations", SetSpecializationsAsync)
            .RequirePermission(DirectoryPermissions.ManageClients)
            .WithName("SetClientSpecializations")
            .WithSummary("Replaces the client's Client specializations")
            .Produces<ClientDetailResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        clients.MapPost("/{id:guid}/invitation", SendInvitationAsync)
            .RequirePermission(DirectoryPermissions.ManageClients)
            .WithName("SendClientInvitation")
            .WithSummary("E-mails a new activation link to a client who has not activated the account")
            .Produces<ClientInvitationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        clients.MapPost("/{id:guid}/password-reset", ResetPasswordAsync)
            .RequirePermission(DirectoryPermissions.ResetClientPasswords)
            .WithName("ResetClientPassword")
            .WithSummary("E-mails a reset link, or sets a temporary password shown once (changed at the next sign-in)")
            .Produces<ClientPasswordResetResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(
        IClientQueryService clients,
        string? view,
        [FromQuery(Name = "filter[fullName]")] string? fullName,
        [FromQuery(Name = "filter[lastName]")] string? lastName,
        [FromQuery(Name = "filter[email]")] string? email,
        [FromQuery(Name = "filter[userName]")] string? userName,
        [FromQuery(Name = "filter[phone]")] string? phone,
        [FromQuery(Name = "filter[status]")] string? status,
        [FromQuery(Name = "filter[employeeUserId]")] Guid? employeeUserId,
        [FromQuery(Name = "filter[tagId]")] Guid? tagId,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await clients.ListAsync(
            new ClientListQuery(view, fullName, lastName, email, userName, phone, status, sort, page ?? 1, pageSize ?? 25, employeeUserId, tagId), cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> AssignableEmployeesAsync(IClientQueryService clients, CancellationToken cancellationToken) =>
        TypedResults.Ok(await clients.AssignableEmployeesAsync(cancellationToken));

    private static async Task<IResult> SpecializationsAsync(IClientQueryService clients, CancellationToken cancellationToken) =>
        TypedResults.Ok(await clients.SpecializationsAsync(cancellationToken));

    private static async Task<IResult> GetAsync(Guid id, IClientQueryService clients, CancellationToken cancellationToken) =>
        (await clients.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> CreateAsync(CreateClientRequest request, IClientManager manager, CancellationToken cancellationToken) =>
        (await manager.CreateAsync(request, cancellationToken))
            .ToHttpResult(created => TypedResults.Created($"/api/v1/clients/{created.Id}", created));

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateClientRequest request, IClientManager manager, IClientQueryService clients, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.UpdateAsync(id, request, cancellationToken), id, clients, cancellationToken);

    private static async Task<IResult> DeleteAsync(Guid id, IClientManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> SetSignInAsync(
        Guid id, SetClientSignInRequest request, IClientManager manager, IClientQueryService clients, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.SetSignInAsync(id, request.CanSignIn, cancellationToken), id, clients, cancellationToken);

    private static async Task<IResult> AssignAsync(
        Guid id, AssignClientEmployeeRequest request, IClientManager manager, IClientQueryService clients, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.AssignAsync(id, request.EmployeeUserId, cancellationToken), id, clients, cancellationToken);

    private static async Task<IResult> UnassignAsync(Guid id, IClientManager manager, IClientQueryService clients, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.UnassignAsync(id, cancellationToken), id, clients, cancellationToken);

    private static async Task<IResult> SetSpecializationsAsync(
        Guid id, SetClientSpecializationsRequest request, IClientManager manager, IClientQueryService clients, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.SetSpecializationsAsync(id, request.SpecializationIds, cancellationToken), id, clients, cancellationToken);

    private static async Task<IResult> SendInvitationAsync(Guid id, IClientManager manager, CancellationToken cancellationToken) =>
        (await manager.SendInvitationAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ResetPasswordAsync(
        Guid id, ResetClientPasswordRequest request, IClientManager manager, HttpContext context, CancellationToken cancellationToken) =>
        (await manager.ResetPasswordAsync(id, request.SendLink, cancellationToken)).ToHttpResult(reset =>
        {
            // A temporary password is shown once: never cached.
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(reset);
        });

    /// <summary>A change answers with the client as it is now.</summary>
    private static async Task<IResult> DetailAfterAsync(Result change, Guid id, IClientQueryService clients, CancellationToken cancellationToken) =>
        change.IsFailure ? change.Error!.ToProblem() : (await clients.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);
}
