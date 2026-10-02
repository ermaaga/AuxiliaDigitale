using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Directory;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Directory;
using Auxilia.SharedKernel.Results;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Directory;

/// <summary>
/// Employees of the tenant for Administrators (F06): list, detail, create, edit, soft delete, sign-in, default
/// employee (Q31), administrator (Q32), specializations, invitation and password reset. An employee is identified by
/// the user id. The clients in charge are the client list filtered by <c>filter[employeeUserId]</c>; assigning and
/// unassigning them goes through <c>/clients/{id}/employee</c>. Module <c>directory</c>: 404 when not visible.
/// </summary>
internal sealed class EmployeeEndpoints : IModuleEndpoints
{
    public string ModuleCode => DirectoryModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var employees = module.MapGroup("/employees").WithTags("Employees");

        employees.MapGet("/", ListAsync)
            .RequirePermission(DirectoryPermissions.ViewEmployees)
            .WithName("ListEmployees")
            .WithSummary("Employees filtered by name, surname, e-mail, user name, phone and status (active/inactive)")
            .Produces<PagedResponse<EmployeeListItemResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        employees.MapGet("/administrators", AdministratorsAsync)
            .RequirePermission(DirectoryPermissions.ViewEmployees)
            .WithName("ListEmployeeAdministrators")
            .WithSummary("Administrators an employee can report to (Administrator role, can sign in)")
            .Produces<IReadOnlyList<EmployeeAdministratorResponse>>();

        employees.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(DirectoryPermissions.ViewEmployees)
            .WithName("GetEmployee")
            .WithSummary("An employee: personal data, account, default flag, administrator, specializations, workload")
            .Produces<EmployeeDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        employees.MapPost("/", CreateAsync)
            .RequirePermission(DirectoryPermissions.ManageEmployees)
            .WithName("CreateEmployee")
            .WithSummary("Creates an employee (e-mail = user name); with sign-in enabled the activation e-mail leaves at once")
            .Produces<CreateEmployeeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        employees.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(DirectoryPermissions.ManageEmployees)
            .WithName("UpdateEmployee")
            .WithSummary("Changes personal data and user name of an employee")
            .Produces<EmployeeDetailResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        employees.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(DirectoryPermissions.ManageEmployees)
            .WithName("DeleteEmployee")
            .WithSummary("Deletes an employee (soft delete): sign-in disabled, clients handed to the default employee")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        employees.MapPut("/{id:guid}/sign-in", SetSignInAsync)
            .RequirePermission(DirectoryPermissions.ManageEmployees)
            .WithName("SetEmployeeSignIn")
            .WithSummary("Enables or disables the employee's sign-in; the default employee cannot be disabled")
            .Produces<EmployeeDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        employees.MapPut("/{id:guid}/default", MakeDefaultAsync)
            .RequirePermission(DirectoryPermissions.ManageEmployees)
            .WithName("SetDefaultEmployee")
            .WithSummary("Makes the employee the default one, who receives the clients created without an employee")
            .Produces<EmployeeDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        employees.MapPut("/{id:guid}/specializations", SetSpecializationsAsync)
            .RequirePermission(DirectoryPermissions.ManageEmployees)
            .WithName("SetEmployeeSpecializations")
            .WithSummary("Replaces the employee's Employee specializations")
            .Produces<EmployeeDetailResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        employees.MapPut("/{id:guid}/administrator", SetAdministratorAsync)
            .RequirePermission(DirectoryPermissions.ManageEmployees)
            .WithName("SetEmployeeAdministrator")
            .WithSummary("Sets the administrator the employee reports to")
            .Produces<EmployeeDetailResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        employees.MapDelete("/{id:guid}/administrator", RemoveAdministratorAsync)
            .RequirePermission(DirectoryPermissions.ManageEmployees)
            .WithName("RemoveEmployeeAdministrator")
            .WithSummary("The employee reports to no administrator from now on")
            .Produces<EmployeeDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        employees.MapPost("/{id:guid}/invitation", SendInvitationAsync)
            .RequirePermission(DirectoryPermissions.ManageEmployees)
            .WithName("SendEmployeeInvitation")
            .WithSummary("E-mails a new activation link to an employee who has not activated the account")
            .Produces<EmployeeInvitationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        employees.MapPost("/{id:guid}/password-reset", ResetPasswordAsync)
            .RequirePermission(DirectoryPermissions.ManageEmployees)
            .WithName("ResetEmployeePassword")
            .WithSummary("E-mails a reset link, or sets a temporary password shown once (changed at the next sign-in)")
            .Produces<EmployeePasswordResetResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(
        IEmployeeQueryService employees,
        [FromQuery(Name = "filter[fullName]")] string? fullName,
        [FromQuery(Name = "filter[lastName]")] string? lastName,
        [FromQuery(Name = "filter[email]")] string? email,
        [FromQuery(Name = "filter[userName]")] string? userName,
        [FromQuery(Name = "filter[phone]")] string? phone,
        [FromQuery(Name = "filter[status]")] string? status,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await employees.ListAsync(
            new EmployeeListQuery(fullName, lastName, email, userName, phone, status, sort, page ?? 1, pageSize ?? 25), cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> AdministratorsAsync(IEmployeeQueryService employees, CancellationToken cancellationToken) =>
        TypedResults.Ok(await employees.AdministratorsAsync(cancellationToken));

    private static async Task<IResult> GetAsync(Guid id, IEmployeeQueryService employees, CancellationToken cancellationToken) =>
        (await employees.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> CreateAsync(CreateEmployeeRequest request, IEmployeeManager manager, CancellationToken cancellationToken) =>
        (await manager.CreateAsync(request, cancellationToken))
            .ToHttpResult(created => TypedResults.Created($"/api/v1/employees/{created.Id}", created));

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateEmployeeRequest request, IEmployeeManager manager, IEmployeeQueryService employees, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.UpdateAsync(id, request, cancellationToken), id, employees, cancellationToken);

    private static async Task<IResult> DeleteAsync(Guid id, IEmployeeManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> SetSignInAsync(
        Guid id, SetEmployeeSignInRequest request, IEmployeeManager manager, IEmployeeQueryService employees, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.SetSignInAsync(id, request.CanSignIn, cancellationToken), id, employees, cancellationToken);

    private static async Task<IResult> MakeDefaultAsync(Guid id, IEmployeeManager manager, IEmployeeQueryService employees, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.MakeDefaultAsync(id, cancellationToken), id, employees, cancellationToken);

    private static async Task<IResult> SetSpecializationsAsync(
        Guid id, SetEmployeeSpecializationsRequest request, IEmployeeManager manager, IEmployeeQueryService employees, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.SetSpecializationsAsync(id, request.SpecializationIds, cancellationToken), id, employees, cancellationToken);

    private static async Task<IResult> SetAdministratorAsync(
        Guid id, SetEmployeeAdministratorRequest request, IEmployeeManager manager, IEmployeeQueryService employees, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.SetAdministratorAsync(id, request.AdministratorUserId, cancellationToken), id, employees, cancellationToken);

    private static async Task<IResult> RemoveAdministratorAsync(Guid id, IEmployeeManager manager, IEmployeeQueryService employees, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.SetAdministratorAsync(id, null, cancellationToken), id, employees, cancellationToken);

    private static async Task<IResult> SendInvitationAsync(Guid id, IEmployeeManager manager, CancellationToken cancellationToken) =>
        (await manager.SendInvitationAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ResetPasswordAsync(
        Guid id, ResetEmployeePasswordRequest request, IEmployeeManager manager, HttpContext context, CancellationToken cancellationToken) =>
        (await manager.ResetPasswordAsync(id, request.SendLink, cancellationToken)).ToHttpResult(reset =>
        {
            // A temporary password is shown once: never cached.
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(reset);
        });

    /// <summary>A change answers with the employee as it is now.</summary>
    private static async Task<IResult> DetailAfterAsync(Result change, Guid id, IEmployeeQueryService employees, CancellationToken cancellationToken) =>
        change.IsFailure ? change.Error!.ToProblem() : (await employees.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);
}
