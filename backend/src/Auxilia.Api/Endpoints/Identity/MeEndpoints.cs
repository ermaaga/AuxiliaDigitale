using System.Security.Claims;

using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.RateLimiting;
using Auxilia.Api.Tenancy;
using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Identity;
using Auxilia.Application.Platform.Modules;
using Auxilia.Contracts.Identity;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Infrastructure.Security.Tokens;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Api.Endpoints.Identity;

/// <summary>
/// The signed-in tenant user (F22, F04): <c>GET /me</c> (roles, effective permissions), <c>GET /me/navigation</c> (menu
/// of the visible modules), the own password, profile, language, theme, picture and sessions. Any authenticated tenant
/// user: no extra permission; everything acts on the caller only.
/// </summary>
internal sealed class MeEndpoints : IApiEndpoints
{
    public void Map(RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Me").RequireTenant().RequireTenantUser();

        me.MapGet(string.Empty, GetMeAsync)
            .WithName("GetMe")
            .WithSummary("The signed-in user, the tenant, every role and the effective permissions")
            .Produces<MeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("/navigation", GetNavigationAsync)
            .WithName("GetMyNavigation")
            .WithSummary("Menu of the tenant app for the signed-in user (visible modules, roles and permissions)")
            .Produces<IReadOnlyList<NavigationItemResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        me.MapPost("/password", ChangePasswordAsync)
            .RequireRateLimiting(RateLimitingSetup.SignInPolicy)
            .WithName("ChangeMyPassword")
            .WithSummary("Changes the own password (current one required); the other sessions of the user end")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        me.MapGet("/profile", GetProfileAsync)
            .WithName("GetMyProfile")
            .WithSummary("The own profile: name, e-mail, phone, read-only user name, language, theme and picture version")
            .Produces<ProfileResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPut("/profile", UpdateProfileAsync)
            .WithName("UpdateMyProfile")
            .WithSummary("Changes the own first and last name, e-mail and phone (the user name stays)")
            .Produces<ProfileResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPut("/language", ChangeLanguageAsync)
            .WithName("ChangeMyLanguage")
            .WithSummary("Sets the own language (an active language of the tenant), also for the next sign-ins")
            .Produces<ProfileResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPut("/preferences", UpdatePreferencesAsync)
            .WithName("UpdateMyPreferences")
            .WithSummary("Sets the own theme: System, Light or Dark")
            .Produces<ProfileResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // A bearer token, not a cookie, authorises the upload: there is no form to forge (the BFF checks its own header).
        me.MapPut("/image", SetImageAsync)
            .DisableAntiforgery()
            .WithName("SetMyImage")
            .WithSummary("Uploads the own picture (JPEG, PNG or WebP, at most 2 MB); stored resized to at most 400 × 400")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<ProfileResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapDelete("/image", RemoveImageAsync)
            .WithName("RemoveMyImage")
            .WithSummary("Removes the own picture")
            .Produces<ProfileResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapGet("/sessions", ListSessionsAsync)
            .WithName("ListMySessions")
            .WithSummary("The own open sessions, most recently used first; the current one is marked")
            .Produces<IReadOnlyList<MySessionResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapDelete("/sessions/{id:guid}", EndSessionAsync)
            .WithName("EndMySession")
            .WithSummary("Ends one of the own sessions: its tokens stop working and its connections sign out")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static Guid? SessionOf(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(TokenClaims.Session), out var sid) ? sid : null;

    private static async Task<IResult> GetProfileAsync(IProfileQueryService profile, CancellationToken cancellationToken) =>
        (await profile.GetAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> UpdateProfileAsync(
        UpdateProfileRequest request, IProfileManager manager, IProfileQueryService profile, CancellationToken cancellationToken) =>
        await ProfileAfterAsync(await manager.UpdateAsync(request, cancellationToken), profile, cancellationToken);

    private static async Task<IResult> ChangeLanguageAsync(
        ChangeLanguageRequest request, IProfileManager manager, IProfileQueryService profile, CancellationToken cancellationToken) =>
        await ProfileAfterAsync(await manager.ChangeLanguageAsync(request.LanguageCode, cancellationToken), profile, cancellationToken);

    private static async Task<IResult> UpdatePreferencesAsync(
        UpdatePreferencesRequest request, IProfileManager manager, IProfileQueryService profile, CancellationToken cancellationToken) =>
        await ProfileAfterAsync(await manager.UpdatePreferencesAsync(request, cancellationToken), profile, cancellationToken);

    private static async Task<IResult> SetImageAsync(IFormFile file, IProfileManager manager, IProfileQueryService profile, CancellationToken cancellationToken)
    {
        if (file.Length > UserImage.UploadMaxBytes)
        {
            return Errors.Identity.ProfileImageInvalid().ToProblem();
        }

        byte[] content;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream((int)file.Length))
        {
            await stream.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        return await ProfileAfterAsync(await manager.SetImageAsync(content, cancellationToken), profile, cancellationToken);
    }

    private static async Task<IResult> RemoveImageAsync(IProfileManager manager, IProfileQueryService profile, CancellationToken cancellationToken) =>
        await ProfileAfterAsync(await manager.RemoveImageAsync(cancellationToken), profile, cancellationToken);

    private static async Task<IResult> ListSessionsAsync(ClaimsPrincipal principal, IProfileQueryService profile, CancellationToken cancellationToken) =>
        (await profile.SessionsAsync(SessionOf(principal), cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> EndSessionAsync(
        Guid id, ICurrentUser currentUser, ISessionManager sessions, CancellationToken cancellationToken)
    {
        if (currentUser.ActorType != ActorType.User || currentUser.UserId is not { } userId)
        {
            return Errors.Identity.UserNotFound().ToProblem();
        }

        return (await sessions.EndOwnSessionAsync(userId, id, cancellationToken)).ToHttpResult(TypedResults.NoContent);
    }

    /// <summary>A change answers with the profile as it is now.</summary>
    private static async Task<IResult> ProfileAfterAsync(Result change, IProfileQueryService profile, CancellationToken cancellationToken) =>
        change.IsFailure ? change.Error!.ToProblem() : (await profile.GetAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request, ICurrentUser currentUser, ClaimsPrincipal principal, ISessionManager sessions, CancellationToken cancellationToken)
    {
        if (currentUser.ActorType != ActorType.User || currentUser.UserId is not { } userId)
        {
            return Errors.Identity.UserNotFound().ToProblem();
        }

        return (await sessions.ChangePasswordAsync(userId, SessionOf(principal), request.CurrentPassword, request.NewPassword, cancellationToken))
            .ToHttpResult(TypedResults.NoContent);
    }

    private static async Task<IResult> GetMeAsync(ICurrentUserQueryService users, CancellationToken cancellationToken) =>
        (await users.GetAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetNavigationAsync(INavigationQueryService navigation, CancellationToken cancellationToken) =>
        TypedResults.Ok(await navigation.GetAsync(cancellationToken));
}
