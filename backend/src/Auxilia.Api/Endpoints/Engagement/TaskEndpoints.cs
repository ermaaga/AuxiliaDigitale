using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Engagement;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Engagement;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Engagement;

/// <summary>
/// Tasks of the staff (B-26) and the client timeline with its activities. A task the caller cannot see is 404.
/// Module <c>engagement</c>: 404 when not visible to the role.
/// </summary>
internal sealed class TaskEndpoints : IModuleEndpoints
{
    public string ModuleCode => EngagementModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var tasks = module.MapGroup("/tasks").WithTags("Tasks");

        tasks.MapGet("/", ListAsync)
            .RequirePermission(EngagementPermissions.ViewTasks)
            .WithName("ListTasks")
            .WithSummary("Tasks given to me (scope=mine) or that I may see (scope=all), by status, client, case or due today")
            .Produces<PagedResponse<TaskResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        tasks.MapGet("/assignees", async (ITaskQueryService query, CancellationToken cancellationToken) => TypedResults.Ok(await query.AssigneesAsync(cancellationToken)))
            .RequirePermission(EngagementPermissions.ManageTasks)
            .WithName("ListTaskAssignees")
            .WithSummary("Staff a task can be given to (active Administrators and Employees), by name")
            .Produces<IReadOnlyList<TaskAssigneeResponse>>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        tasks.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(EngagementPermissions.ViewTasks)
            .WithName("GetTask")
            .WithSummary("A task")
            .Produces<TaskResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        tasks.MapPost("/", CreateAsync)
            .RequirePermission(EngagementPermissions.ManageTasks)
            .WithName("CreateTask")
            .WithSummary("Creates a task; the assignee is notified unless it is the caller")
            .Produces<TaskResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        tasks.MapPut("/{id:guid}", UpdateAsync)
            .RequirePermission(EngagementPermissions.ManageTasks)
            .WithName("UpdateTask")
            .WithSummary("Changes a task; a new assignee is notified")
            .Produces<TaskResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        tasks.MapPost("/{id:guid}/complete", CompleteAsync)
            .RequirePermission(EngagementPermissions.ManageTasks)
            .WithName("CompleteTask")
            .WithSummary("Marks a task as done")
            .Produces<TaskResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        tasks.MapPost("/{id:guid}/reopen", ReopenAsync)
            .RequirePermission(EngagementPermissions.ManageTasks)
            .WithName("ReopenTask")
            .WithSummary("Opens a done task again")
            .Produces<TaskResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        tasks.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(EngagementPermissions.ManageTasks)
            .WithName("DeleteTask")
            .WithSummary("Deletes a task (soft delete)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var clients = module.MapGroup("/clients").WithTags("Tasks");

        clients.MapGet("/{id:guid}/timeline", TimelineAsync)
            .RequirePermission(EngagementPermissions.ViewActivities)
            .WithName("GetClientTimeline")
            .WithSummary("The timeline of a client, newest first: activities, tasks and the cases the caller sees (F10); before = the at of the last entry")
            .Produces<IReadOnlyList<TimelineEntryResponse>>()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        clients.MapPost("/{id:guid}/activities", AddActivityAsync)
            .RequirePermission(EngagementPermissions.ManageActivities)
            .WithName("AddClientActivity")
            .WithSummary("Writes a note, call, meeting or e-mail on a client")
            .Produces<AddActivityResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        module.MapGroup("/activities").WithTags("Tasks").MapDelete("/{id:guid}", DeleteActivityAsync)
            .RequirePermission(EngagementPermissions.ManageActivities)
            .WithName("DeleteClientActivity")
            .WithSummary("Deletes an activity (its author or an Administrator)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(
        ITaskQueryService query,
        string? scope,
        [FromQuery(Name = "filter[status]")] string? status,
        [FromQuery(Name = "filter[clientId]")] Guid? clientId,
        [FromQuery(Name = "filter[caseId]")] Guid? caseId,
        [FromQuery(Name = "filter[due]")] string? due,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await query.ListAsync(new TaskListQuery(scope, status, clientId, caseId, due, sort, page ?? 1, pageSize ?? 25), cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetAsync(Guid id, ITaskQueryService query, CancellationToken cancellationToken) =>
        (await query.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> CreateAsync(SaveTaskRequest request, ITaskManager manager, ITaskQueryService query, CancellationToken cancellationToken)
    {
        var created = await manager.CreateAsync(request, cancellationToken);
        return created.IsFailure
            ? created.Error!.ToProblem()
            : (await query.GetAsync(created.Value, cancellationToken)).ToHttpResult(task => TypedResults.Created($"/api/v1/tasks/{task.Id}", task));
    }

    private static async Task<IResult> UpdateAsync(Guid id, SaveTaskRequest request, ITaskManager manager, ITaskQueryService query, CancellationToken cancellationToken) =>
        await AfterAsync(await manager.UpdateAsync(id, request, cancellationToken), id, query, cancellationToken);

    private static async Task<IResult> CompleteAsync(Guid id, ITaskManager manager, ITaskQueryService query, CancellationToken cancellationToken) =>
        await AfterAsync(await manager.CompleteAsync(id, cancellationToken), id, query, cancellationToken);

    private static async Task<IResult> ReopenAsync(Guid id, ITaskManager manager, ITaskQueryService query, CancellationToken cancellationToken) =>
        await AfterAsync(await manager.ReopenAsync(id, cancellationToken), id, query, cancellationToken);

    private static async Task<IResult> DeleteAsync(Guid id, ITaskManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> TimelineAsync(Guid id, ITimelineQueryService query, DateTimeOffset? before, int? take, CancellationToken cancellationToken) =>
        (await query.TimelineAsync(id, before, take ?? 30, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> AddActivityAsync(Guid id, AddActivityRequest request, IActivityManager manager, CancellationToken cancellationToken) =>
        (await manager.AddAsync(id, request, cancellationToken)).ToHttpResult(created => TypedResults.Created($"/api/v1/clients/{id}/timeline", new AddActivityResponse(created)));

    private static async Task<IResult> DeleteActivityAsync(Guid id, IActivityManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> AfterAsync(SharedKernel.Results.Result change, Guid id, ITaskQueryService query, CancellationToken cancellationToken) =>
        change.IsFailure ? change.Error!.ToProblem() : (await query.GetAsync(id, cancellationToken)).ToHttpResult(TypedResults.Ok);
}
