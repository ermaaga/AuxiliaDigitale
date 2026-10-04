using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Reporting;
using Auxilia.Contracts.Reporting;

namespace Auxilia.Api.Endpoints.Reporting;

/// <summary>The dashboard of the caller's role (F27): one endpoint, widgets from every visible module. Module <c>reporting</c>.</summary>
internal sealed class DashboardEndpoints : IModuleEndpoints
{
    public string ModuleCode => ReportingModule.ModuleCode;

    public void Map(RouteGroupBuilder module) =>
        module.MapGet("/dashboard", GetAsync)
            .WithTags("Dashboard")
            .RequirePermission(ReportingPermissions.ViewDashboard)
            .WithName("GetDashboard")
            .WithSummary("Cards, charts and lists of the caller's role; period week, month (default), year or all")
            .Produces<DashboardResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<IResult> GetAsync(IDashboardQueryService dashboard, string? period, CancellationToken cancellationToken) =>
        (await dashboard.GetAsync(period, cancellationToken)).ToHttpResult(TypedResults.Ok);
}
