using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Scheduling;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Scheduling;
using Auxilia.SharedKernel.Results;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Scheduling;

/// <summary>
/// Appointments (F13): grid, calendar ranges (own or the global staff calendar), detail with timeline; staff schedule,
/// move, approve, reject, complete and delete; clients request and cancel. An appointment the caller cannot see is 404,
/// one it sees but may not change is 403. Module <c>scheduling</c>: 404 when not visible to the role.
/// </summary>
internal sealed class AppointmentEndpoints : IModuleEndpoints
{
    public string ModuleCode => SchedulingModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var appointments = module.MapGroup("/appointments").WithTags("Appointments");

        appointments.MapGet("/", ListAsync)
            .RequirePermission(SchedulingPermissions.ViewAppointments)
            .WithName("ListAppointments")
            .WithSummary("The appointments the caller may see, filtered by client, employee, status and local days")
            .Produces<PagedResponse<AppointmentListItemResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        appointments.MapGet("/calendar", CalendarAsync)
            .RequirePermission(SchedulingPermissions.ViewAppointments)
            .WithName("GetAppointmentCalendar")
            .WithSummary("The appointments of a range of local days (at most 62): the caller's own or, for staff, the global calendar")
            .Produces<AppointmentCalendarResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        appointments.MapGet("/conflicts", ConflictsAsync)
            .RequirePermission(SchedulingPermissions.ManageAppointments)
            .WithName("ListAppointmentConflicts")
            .WithSummary("Staff: the open appointments of an employee overlapping a slot (a warning, never a block)")
            .Produces<IReadOnlyList<AppointmentConflictResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        appointments.MapGet("/employees", EmployeesAsync)
            .RequirePermission(SchedulingPermissions.ViewAppointments)
            .WithName("ListAppointmentEmployees")
            .WithSummary("Clients: the active employees an appointment can be requested with, the one in charge marked")
            .Produces<IReadOnlyList<AppointmentEmployeeChoiceResponse>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        appointments.MapGet("/{id:guid}", GetAsync)
            .WithETag()
            .RequirePermission(SchedulingPermissions.ViewAppointments)
            .WithName("GetAppointment")
            .WithSummary("An appointment with its timeline, conflict warning (staff) and what the caller may do")
            .Produces<AppointmentResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        appointments.MapPost("/", ScheduleAsync)
            .RequirePermission(SchedulingPermissions.ManageAppointments)
            .WithName("ScheduleAppointment")
            .WithSummary("Staff schedule an appointment for a client (approved; employees only for the clients in charge)")
            .Produces<AppointmentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        appointments.MapPost("/requests", RequestAsync)
            .RequirePermission(SchedulingPermissions.ManageAppointments)
            .WithName("RequestAppointment")
            .WithSummary("A client requests an appointment with an employee (pending until approved or rejected)")
            .Produces<AppointmentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        appointments.MapPut("/{id:guid}", UpdateAsync)
            .RequireIfMatch()
            .RequirePermission(SchedulingPermissions.ManageAppointments)
            .WithName("UpdateAppointment")
            .WithSummary("Moves or edits a pending or approved appointment (a new start must be in the future)")
            .Produces<AppointmentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        MapStatus(appointments, "approve", "ApproveAppointment", "Approves a pending appointment", (manager, id, note, ct) => manager.ApproveAsync(id, note, ct));
        MapStatus(appointments, "reject", "RejectAppointment", "Rejects a pending appointment", (manager, id, note, ct) => manager.RejectAsync(id, note, ct));
        MapStatus(appointments, "complete", "CompleteAppointment", "Completes an approved appointment", (manager, id, note, ct) => manager.CompleteAsync(id, note, ct));
        MapStatus(appointments, "cancel", "CancelAppointment", "Cancels a pending or approved appointment (staff or the client)", (manager, id, note, ct) => manager.CancelAsync(id, note, ct));

        appointments.MapDelete("/{id:guid}", DeleteAsync)
            .RequireIfMatch()
            .RequirePermission(SchedulingPermissions.ManageAppointments)
            .WithName("DeleteAppointment")
            .WithSummary("Staff delete an appointment (soft delete, history kept)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static void MapStatus(
        RouteGroupBuilder appointments,
        string action,
        string name,
        string summary,
        Func<IAppointmentManager, Guid, string?, CancellationToken, Task<Result>> change) =>
        appointments.MapPost($"/{{id:guid}}/{action}", async (
                Guid id, ChangeAppointmentStatusRequest? request, IAppointmentManager manager, IAppointmentQueryService queries, CancellationToken cancellationToken) =>
                await DetailAfterAsync(await change(manager, id, request?.Note, cancellationToken), id, queries, cancellationToken))
            .RequirePermission(SchedulingPermissions.ManageAppointments)
            .WithName(name)
            .WithSummary(summary)
            .Produces<AppointmentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> ListAsync(
        IAppointmentQueryService appointments,
        [FromQuery(Name = "filter[clientId]")] Guid? clientId,
        [FromQuery(Name = "filter[employeeUserId]")] Guid? employeeUserId,
        [FromQuery(Name = "filter[status]")] string? status,
        DateOnly? from,
        DateOnly? to,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await appointments.ListAsync(new AppointmentListQuery(clientId, employeeUserId, status, from, to, sort, page ?? 1, pageSize ?? 25), cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> CalendarAsync(
        IAppointmentQueryService appointments,
        DateOnly from,
        DateOnly to,
        bool? global,
        Guid? clientId,
        Guid? employeeUserId,
        CancellationToken cancellationToken) =>
        (await appointments.CalendarAsync(new AppointmentCalendarQuery(from, to, global ?? false, clientId, employeeUserId), cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ConflictsAsync(
        IAppointmentQueryService appointments,
        Guid employeeUserId,
        DateOnly date,
        TimeOnly time,
        int durationMinutes,
        Guid? excludeId,
        CancellationToken cancellationToken) =>
        (await appointments.ConflictsAsync(new AppointmentConflictQuery(employeeUserId, date, time, durationMinutes, excludeId), cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> EmployeesAsync(IAppointmentQueryService appointments, CancellationToken cancellationToken) =>
        (await appointments.EmployeesAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetAsync(Guid id, IAppointmentQueryService appointments, CancellationToken cancellationToken) =>
        (await appointments.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ScheduleAsync(
        ScheduleAppointmentRequest request, IAppointmentManager manager, IAppointmentQueryService appointments, CancellationToken cancellationToken) =>
        await CreatedAsync(await manager.ScheduleAsync(request, cancellationToken), appointments, cancellationToken);

    private static async Task<IResult> RequestAsync(
        RequestAppointmentRequest request, IAppointmentManager manager, IAppointmentQueryService appointments, CancellationToken cancellationToken) =>
        await CreatedAsync(await manager.RequestAsync(request, cancellationToken), appointments, cancellationToken);

    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateAppointmentRequest request, IAppointmentManager manager, IAppointmentQueryService appointments, CancellationToken cancellationToken) =>
        await DetailAfterAsync(await manager.UpdateAsync(id, request, cancellationToken), id, appointments, cancellationToken);

    private static async Task<IResult> DeleteAsync(Guid id, IAppointmentManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> CreatedAsync(Result<Guid> created, IAppointmentQueryService appointments, CancellationToken cancellationToken) =>
        created.IsFailure
            ? created.Error!.ToProblem()
            : (await appointments.GetAsync(created.Value, cancellationToken)).ToHttpResult(detail => TypedResults.Created($"/api/v1/appointments/{detail.Id}", detail));

    /// <summary>A change answers with the appointment as it is now.</summary>
    private static async Task<IResult> DetailAfterAsync(Result change, Guid id, IAppointmentQueryService appointments, CancellationToken cancellationToken) =>
        change.IsFailure ? change.Error!.ToProblem() : (await appointments.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);
}
