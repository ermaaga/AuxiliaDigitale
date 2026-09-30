using System.Security.Claims;

using Auxilia.Api.Authorization;
using Auxilia.Api.Endpoints.Identity;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.RateLimiting;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Identity;
using Auxilia.Application.Platform;
using Auxilia.Contracts.Identity;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Infrastructure.Security.Tokens;

namespace Auxilia.Api.Endpoints.Platform;

/// <summary>
/// The platform console API (N02, System role): <c>/api/v1/platform/…</c>. Sign-in with password + TOTP (D-22) on a
/// <c>PlatformConsole</c> client, the signed-in user, the tenant list, and the tenant-scoped platform token that opens
/// one tenant's technical endpoints. No endpoint here reads business data (D-21).
/// </summary>
internal sealed class PlatformEndpoints : IApiEndpoints
{
    public void Map(RouteGroupBuilder api)
    {
        var platform = api.MapGroup("/platform").WithTags("Platform");
        var auth = platform.MapGroup("/auth");

        // Anonymous: the activation token printed by auxctl is the credential.
        auth.MapPost("/enrollment", BeginEnrollmentAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.AccountLinksPolicy)
            .WithName("BeginPlatformEnrollment")
            .WithSummary("Starts the TOTP enrolment of a platform user with the activation token (secret shown once)")
            .Produces<PlatformEnrollmentResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/activate", ActivateAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.AccountLinksPolicy)
            .WithName("ActivatePlatformAccount")
            .WithSummary("Sets the password and confirms the authenticator with a current code")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        // Anonymous: this is the sign-in.
        auth.MapPost("/token", IssueTokensAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.SignInPolicy)
            .WithName("IssuePlatformTokens")
            .WithSummary("Console sign-in with password and authenticator code, or refresh of the console tokens")
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/logout", LogoutAsync)
            .RequirePlatformUser()
            .WithName("PlatformLogout")
            .WithSummary("Ends the console session and revokes its tokens (tenant-scoped ones included)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        platform.MapGet("/me", GetMeAsync)
            .RequirePlatformUser()
            .WithName("GetPlatformMe")
            .WithSummary("The signed-in platform user")
            .Produces<PlatformMeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        platform.MapGet("/tenants", ListTenantsAsync)
            .RequirePlatformUser()
            .WithName("ListPlatformTenants")
            .WithSummary("Tenants of the platform (archived excluded)")
            .Produces<IReadOnlyList<PlatformTenantResponse>>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        platform.MapPost("/tenants/{slug}/token", OpenTenantAsync)
            .RequirePlatformUser()
            .WithName("OpenPlatformTenant")
            .WithSummary("A short-lived platform token for the technical endpoints of one tenant")
            .Produces<PlatformTenantTokenResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> BeginEnrollmentAsync(PlatformEnrollmentRequest request, IPlatformAuthManager auth, HttpContext context, CancellationToken cancellationToken) =>
        (await auth.BeginEnrollmentAsync(request.ActivationToken, cancellationToken)).ToHttpResult(enrollment =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new PlatformEnrollmentResponse(enrollment.Secret, enrollment.Uri));
        });

    private static async Task<IResult> ActivateAsync(PlatformActivateRequest request, IPlatformAuthManager auth, CancellationToken cancellationToken) =>
        (await auth.ActivateAsync(request.ActivationToken, request.Password, request.Code, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> IssueTokensAsync(PlatformTokenRequest request, HttpContext context, IPlatformAuthManager auth, CancellationToken cancellationToken)
    {
        var client = new ClientCredentials(
            context.Request.Headers[AuthEndpoints.ClientIdHeader].ToString(),
            context.Request.Headers[AuthEndpoints.ClientSecretHeader].FirstOrDefault());
        if (string.IsNullOrWhiteSpace(client.ClientId))
        {
            return Errors.Identity.ClientInvalid().ToProblem();
        }

        var ipAddress = context.Connection.RemoteIpAddress?.ToString();
        var userAgent = context.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent
            ? agent[..Math.Min(agent.Length, RefreshSession.UserAgentMaxLength)]
            : null;
        var result = request.GrantType switch
        {
            AuthEndpoints.PasswordGrant when !string.IsNullOrEmpty(request.Email) && !string.IsNullOrEmpty(request.Password) && !string.IsNullOrEmpty(request.Code) =>
                await auth.SignInAsync(new PlatformSignIn(client, request.Email, request.Password, request.Code, ipAddress, userAgent), cancellationToken),
            AuthEndpoints.RefreshTokenGrant when !string.IsNullOrEmpty(request.RefreshToken) =>
                await auth.RefreshAsync(new RefreshTokens(client, request.RefreshToken, ipAddress, userAgent), cancellationToken),
            _ => Errors.Host.ValidationFailed(new Dictionary<string, string[]> { ["grantType"] = ["validation.auth.grantType"] }),
        };

        return result.ToHttpResult(pair =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var expiresIn = (int)Math.Max(0, (pair.AccessTokenExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
            return TypedResults.Ok(new TokenResponse(pair.AccessToken, "Bearer", expiresIn, pair.RefreshToken));
        });
    }

    private static async Task<IResult> LogoutAsync(ClaimsPrincipal user, IPlatformAuthManager auth, IAccessTokenDenyList denyList, CancellationToken cancellationToken)
    {
        if (user.FindFirstValue(TokenClaims.TokenId) is { } tokenId && long.TryParse(user.FindFirstValue("exp"), out var exp))
        {
            await denyList.DenyTokenAsync(IAccessTokenDenyList.PlatformNamespace, tokenId, DateTimeOffset.FromUnixTimeSeconds(exp), cancellationToken);
        }

        if (Guid.TryParse(user.FindFirstValue(TokenClaims.Session), out var sessionId))
        {
            _ = await auth.EndSessionAsync(sessionId, SessionEndReason.Logout, cancellationToken);
        }

        return TypedResults.NoContent();
    }

    private static async Task<IResult> GetMeAsync(IPlatformConsoleQueryService console, CancellationToken cancellationToken) =>
        (await console.GetMeAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> ListTenantsAsync(IPlatformConsoleQueryService console, CancellationToken cancellationToken) =>
        TypedResults.Ok(await console.ListTenantsAsync(cancellationToken));

    private static async Task<IResult> OpenTenantAsync(string slug, ClaimsPrincipal user, HttpContext context, IPlatformAuthManager auth, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirstValue(TokenClaims.Session), out var sessionId) || user.FindFirstValue(TokenClaims.Client) is not { } clientId)
        {
            return Errors.Identity.PlatformAccessRequired().ToProblem();
        }

        return (await auth.OpenTenantAsync(slug, sessionId, clientId, cancellationToken)).ToHttpResult(issued =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var expiresIn = (int)Math.Max(0, (issued.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
            return TypedResults.Ok(new PlatformTenantTokenResponse(issued.Token, "Bearer", expiresIn, slug.Trim().ToLowerInvariant()));
        });
    }
}
