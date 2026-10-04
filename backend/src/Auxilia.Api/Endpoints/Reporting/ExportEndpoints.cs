using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Reporting;
using Auxilia.Contracts.Reporting;

namespace Auxilia.Api.Endpoints.Reporting;

/// <summary>
/// Exports (F26, F07): <c>GET /exports/{source}</c> with the list's own query string plus <c>format</c>,
/// <c>columns</c>, <c>ids</c>, <c>language</c> answers the file (small) or 202 with the queued export (large); the
/// caller's queued exports and their files. Each list checks its own permission and visibility. Module
/// <c>reporting</c>.
/// </summary>
internal sealed class ExportEndpoints : IModuleEndpoints
{
    public string ModuleCode => ReportingModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var exports = module.MapGroup("/exports").WithTags("Exports");

        exports.MapGet("/", MineAsync)
            .RequirePermission(ReportingPermissions.UseExports)
            .WithName("ListMyExports")
            .WithSummary("The caller's queued exports of the last 24 hours")
            .Produces<IReadOnlyList<ExportJobResponse>>();

        exports.MapGet("/sources", SourcesAsync)
            .RequirePermission(ReportingPermissions.UseExports)
            .WithName("ListExportSources")
            .WithSummary("The lists the caller may export, with their columns")
            .Produces<IReadOnlyList<ExportSourceResponse>>();

        exports.MapGet("/{id:guid}/file", FileAsync)
            .RequirePermission(ReportingPermissions.UseExports)
            .WithName("DownloadExport")
            .WithSummary("The file of a ready export of the caller")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        exports.MapGet("/{source}", ExportAsync)
            .RequirePermission(ReportingPermissions.UseExports)
            .WithName("ExportList")
            .WithSummary("Every row of a list's filters (or the ids given) as CSV, Excel or PDF; 202 when queued for the Worker")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .Produces<ExportQueuedResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ExportAsync(string source, HttpRequest request, IExportManager exports, CancellationToken cancellationToken)
    {
        var query = request.Query;
        var parameters = query.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString(), StringComparer.Ordinal);
        var outcome = await exports.ExportAsync(
            new ExportRequest(source, query["format"], query["columns"], query["ids"], query["language"], parameters), cancellationToken);
        if (outcome.IsFailure)
        {
            return outcome.Error!.ToProblem();
        }

        return outcome.Value.File is { } file
            ? TypedResults.File(file.Content, file.ContentType, file.FileName)
            : TypedResults.Accepted($"/api/v1/exports/{outcome.Value.Queued!.Id}/file", outcome.Value.Queued);
    }

    private static async Task<IResult> MineAsync(IExportQueryService exports, CancellationToken cancellationToken) =>
        (await exports.MineAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> SourcesAsync(IExportQueryService exports, CancellationToken cancellationToken) =>
        TypedResults.Ok(await exports.SourcesAsync(cancellationToken));

    private static async Task<IResult> FileAsync(Guid id, IExportQueryService exports, CancellationToken cancellationToken)
    {
        var file = await exports.FileAsync(id, cancellationToken);
        return file.IsFailure ? file.Error!.ToProblem() : TypedResults.File(file.Value.Content, file.Value.ContentType, file.Value.FileName);
    }
}
