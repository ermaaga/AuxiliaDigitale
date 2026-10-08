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

        me.MapGet("/two-factor", GetTwoFactorAsync)
            .WithName("GetMyTwoFactor")
            .WithSummary("Whether the own authenticator app is set and whether the user's roles require it (N04)")
            .Produces<TwoFactorStatusResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        me.MapPost("/two-factor/enrollment", BeginTwoFactorEnrollmentAsync)
            .WithName("BeginMyTwoFactorEnrollment")
            .WithSummary("A new secret of the authenticator app (setup key and QR code URI, shown once) to confirm with a code")
            .Produces<TwoFactorEnrollmentResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        me.MapPost("/two-factor/confirm", ConfirmTwoFactorAsync)
            .RequireRateLimiting(RateLimitingSetup.SignInPolicy)
            .WithName("ConfirmMyTwoFactor")
            .WithSummary("Confirms the enrolment with a code of the app: from the next sign-in the code is asked")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        me.MapPost("/two-factor/disable", DisableTwoFactorAsync)
            .RequireRateLimiting(RateLimitingSetup.SignInPolicy)
            .WithName("DisableMyTwoFactor")
            .WithSummary("Removes the own authenticator app (current password required; refused when the roles require it)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
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

    private static async Task<IResult> GetTwoFactorAsync(ICurrentUser currentUser, ITwoFactorManager twoFactor, CancellationToken cancellationToken) =>
        currentUser.UserId is not { } userId
            ? Errors.Identity.UserNotFound().ToProblem()
            : (await twoFactor.StatusAsync(userId, cancellationToken))
                .ToHttpResult(status => TypedResults.Ok(new TwoFactorStatusResponse(status.Enabled, status.Required, status.EnabledAt)));

    private static async Task<IResult> BeginTwoFactorEnrollmentAsync(
        ICurrentUser currentUser, ITwoFactorManager twoFactor, HttpContext context, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Errors.Identity.UserNotFound().ToProblem();
        }

        return (await twoFactor.BeginEnrollmentAsync(userId, cancellationToken)).ToHttpResult(enrollment =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new TwoFactorEnrollmentResponse(enrollment.Secret, enrollment.Uri));
        });
    }

    private static async Task<IResult> ConfirmTwoFactorAsync(
        ConfirmTwoFactorRequest request, ICurrentUser currentUser, ITwoFactorManager twoFactor, CancellationToken cancellationToken) =>
        currentUser.UserId is not { } userId
            ? Errors.Identity.UserNotFound().ToProblem()
            : (await twoFactor.ConfirmEnrollmentAsync(userId, request.Code, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> DisableTwoFactorAsync(
        DisableTwoFactorRequest request, ICurrentUser currentUser, ITwoFactorManager twoFactor, CancellationToken cancellationToken) =>
        currentUser.UserId is not { } userId
            ? Errors.Identity.UserNotFound().ToProblem()
            : (await twoFactor.DisableAsync(userId, request.Password, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> GetMeAsync(ICurrentUserQueryService users, CancellationToken cancellationToken) =>
        (await users.GetAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> GetNavigationAsync(INavigationQueryService navigation, CancellationToken cancellationToken) =>
        TypedResults.Ok(await navigation.GetAsync(cancellationToken));
}
