using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Identity;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Identity;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Identity;

/// <summary>Endpoints of the Identity module for tenant staff (F35 login audit).</summary>
internal sealed class IdentityModuleEndpoints : IModuleEndpoints
{
    public string ModuleCode => IdentityModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var identity = module.MapGroup("/identity").WithTags("Identity");

        identity.MapGet("/login-attempts", ListLoginAttemptsAsync)
            .RequirePermission(IdentityPermissions.ViewLoginAttempts)
            .WithName("ListLoginAttempts")
            .WithSummary("Login audit: every sign-in attempt, filtered by user name, method, result and date range")
            .Produces<PagedResponse<LoginAttemptResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ListLoginAttemptsAsync(
        ILoginAuditQueryService audit,
        [FromQuery(Name = "filter[userName]")] string? userName,
        [FromQuery(Name = "filter[method]")] string? method,
        [FromQuery(Name = "filter[succeeded]")] bool? succeeded,
        [FromQuery(Name = "filter[from]")] DateTimeOffset? from,
        [FromQuery(Name = "filter[to]")] DateTimeOffset? to,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await audit.ListAttemptsAsync(new LoginAttemptQuery(userName, method, succeeded, from, to, sort, page ?? 1, pageSize ?? 25), cancellationToken))
            .ToHttpResult(TypedResults.Ok);
}
