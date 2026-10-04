using System.Security.Claims;

using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Identity;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Identity;
using Auxilia.Infrastructure.Security.Tokens;

using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Identity;

/// <summary>Endpoints of the Identity module for tenant staff (F35 login audit, F17 active sessions).</summary>
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

        identity.MapGet("/sessions", ListSessionsAsync)
            .RequirePermission(IdentityPermissions.ViewSessions)
            .WithName("ListActiveSessions")
            .WithSummary("The open sessions of the tenant: user, roles, client app, IP, user agent, start, last use, expiry (F17)")
            .Produces<PagedResponse<ActiveSessionResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        identity.MapGet("/sessions/summary", SessionSummaryAsync)
            .RequirePermission(IdentityPermissions.ViewSessions)
            .WithName("GetActiveSessionSummary")
            .WithSummary("The cards of the sessions page: open sessions, distinct users, sessions used in the last 5 minutes")
            .Produces<ActiveSessionSummaryResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        identity.MapDelete("/sessions/{id:guid}", RevokeSessionAsync)
            .RequirePermission(IdentityPermissions.RevokeSessions)
            .WithName("RevokeSession")
            .WithSummary("Ends a user's session: its tokens are denied at once and its connections receive ForceLogout")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListSessionsAsync(
        ClaimsPrincipal principal,
        IActiveSessionQueryService sessions,
        [FromQuery(Name = "filter[userName]")] string? userName,
        string? sort,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken) =>
        (await sessions.ListAsync(
            new ActiveSessionQuery(userName, sort, page ?? 1, pageSize ?? 25),
            Guid.TryParse(principal.FindFirstValue(TokenClaims.Session), out var current) ? current : null,
            cancellationToken))
            .ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> SessionSummaryAsync(IActiveSessionQueryService sessions, CancellationToken cancellationToken) =>
        TypedResults.Ok(await sessions.SummaryAsync(cancellationToken));

    private static async Task<IResult> RevokeSessionAsync(Guid id, ISessionManager sessions, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        (await sessions.RevokeAsync(id, currentUser.UserId, cancellationToken)).ToHttpResult(TypedResults.NoContent);

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
