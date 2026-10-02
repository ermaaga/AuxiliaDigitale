using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Platform;
using Auxilia.Contracts.Platform;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Platform;

/// <summary>
/// The Log page of the platform console (F25, D-17, D-28): a tenant's daily log files and its temporary debug level.
/// Console tokens only; the files are read where the pipeline writes them, the level lives in the Catalog.
/// </summary>
internal sealed class TenantLogEndpoints : IApiEndpoints
{
    public void Map(RouteGroupBuilder api)
    {
        var tenant = api.MapGroup("/platform/tenants/{slug}").WithTags("Platform").RequirePlatformUser();

        tenant.MapGet("/logs", SearchAsync)
            .WithName("SearchPlatformTenantLogs")
            .WithSummary("Events of the tenant's daily log files, newest first, by date range (UTC days, at most 31) and filters")
            .Produces<TenantLogPageResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        tenant.MapGet("/log-level", GetLevelAsync)
            .WithName("GetPlatformTenantLogLevel")
            .WithSummary("The default log level and, while it runs, the end of the tenant's debug logging")
            .Produces<TenantLogLevelResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        tenant.MapPut("/log-level", EnableDebugAsync)
            .WithName("EnablePlatformTenantDebugLogging")
            .WithSummary("Writes the tenant's Debug events until the given instant (within 24 hours); it ends by itself")
            .Produces<TenantLogLevelResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        tenant.MapDelete("/log-level", DisableDebugAsync)
            .WithName("DisablePlatformTenantDebugLogging")
            .WithSummary("Back to the default log level for the tenant now")
            .Produces<TenantLogLevelResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> SearchAsync(
        string slug,
        ITenantLogQueryService logs,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? level = null,
        [FromQuery] string? code = null,
        [FromQuery] string? traceId = null,
        [FromQuery] string? userId = null,
        [FromQuery] string? text = null,
        [FromQuery] string? cursor = null,
        [FromQuery] int? pageSize = null) =>
        (await logs.SearchAsync(slug, new TenantLogQuery(from, to, level, code, traceId, userId, text, cursor, pageSize), cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetLevelAsync(string slug, ITenantLogQueryService logs, CancellationToken cancellationToken) =>
        (await logs.GetLevelAsync(slug, cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> EnableDebugAsync(
        string slug, EnableTenantDebugLoggingRequest request, ITenantLogManager manager, ITenantLogQueryService logs, CancellationToken cancellationToken)
    {
        var changed = await manager.EnableDebugAsync(slug, request, cancellationToken);
        return changed.IsFailure ? changed.Error!.ToProblem() : await GetLevelAsync(slug, logs, cancellationToken);
    }

    private static async Task<IResult> DisableDebugAsync(string slug, ITenantLogManager manager, ITenantLogQueryService logs, CancellationToken cancellationToken)
    {
        var changed = await manager.DisableDebugAsync(slug, cancellationToken);
        return changed.IsFailure ? changed.Error!.ToProblem() : await GetLevelAsync(slug, logs, cancellationToken);
    }
}
