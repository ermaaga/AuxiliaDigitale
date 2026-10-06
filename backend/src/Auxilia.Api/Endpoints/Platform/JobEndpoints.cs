using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Jobs;
using Auxilia.Contracts.Platform;

namespace Auxilia.Api.Endpoints.Platform;

/// <summary>
/// Recurring jobs of a tenant (N02, D-15: never scheduled): the registered jobs with their last run, the latest runs,
/// and "run now", which the Worker carries out. Technical: the System with a platform token scoped to the tenant (D-21).
/// </summary>
internal sealed class JobEndpoints : IApiEndpoints
{
    public void Map(RouteGroupBuilder api)
    {
        var jobs = api.MapGroup("/jobs").WithTags("Jobs").RequirePlatformTenant();

        jobs.MapGet(string.Empty, async (IJobQueryService query, CancellationToken cancellationToken) => TypedResults.Ok(await query.ListAsync(cancellationToken)))
            .WithName("ListJobs")
            .WithSummary("The recurring jobs of the tenant by code, with their last run and whether one is running")
            .Produces<IReadOnlyList<JobResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        jobs.MapGet("/runs", async (IJobQueryService query, int? take, CancellationToken cancellationToken) =>
                TypedResults.Ok(await query.RunsAsync(take ?? 20, cancellationToken)))
            .WithName("ListJobRuns")
            .WithSummary("The latest runs of every job, newest first (take: 1–100, default 20)")
            .Produces<IReadOnlyList<JobRunResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        jobs.MapPost("/{code}/run", async (string code, IJobManager manager, CancellationToken cancellationToken) =>
                (await manager.RequestRunAsync(code, cancellationToken)).ToHttpResult(() => TypedResults.Accepted("/api/v1/jobs/runs")))
            .WithName("RunJob")
            .WithSummary("Queues a run of the job for the Worker; a run already in progress makes it a no-op")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
