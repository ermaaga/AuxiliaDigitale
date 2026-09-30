using System.Security.Claims;

using Auxilia.Api.Infrastructure;
using Auxilia.Api.RateLimiting;
using Auxilia.Api.Tenancy;
using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Identity;
using Auxilia.Contracts.Identity;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Infrastructure.Security.Tokens;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Endpoints.Identity;

/// <summary>
/// Sign-in and account links of a tenant (F01, F17, skill auxilia-security): <c>/api/v1/auth/…</c>. The client
/// application is identified by headers <c>X-Client-Id</c> / <c>X-Client-Secret</c>; the tenant as for every tenant
/// endpoint (host or <c>X-Tenant</c>). Tokens are never logged.
/// </summary>
internal sealed class AuthEndpoints : IApiEndpoints
{
    public const string ClientIdHeader = "X-Client-Id";

    public const string ClientSecretHeader = "X-Client-Secret";

    public const string PasswordGrant = "password";

    public const string RefreshTokenGrant = "refresh_token";

    public const string EmailOtpGrant = "email_otp";

    public void Map(RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Auth").RequireTenant();

        auth.MapPost("/token", IssueTokensAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.SignInPolicy)
            .WithName("IssueTokens")
            .WithSummary("Signs in with a password or an e-mailed code, or exchanges a refresh token for a new token pair")
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        auth.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Ends the session of the access token and revokes its tokens")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        auth.MapPost("/activate", ActivateAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.AccountLinksPolicy)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithName("ActivateAccount")
            .WithSummary("Activates an account with the token of the activation link and sets its password")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        auth.MapPost("/password/forgot", ForgotPasswordAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.AccountLinksPolicy)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithName("ForgotPassword")
            .WithSummary("Sends a password reset link when the user exists (always 202)");

        auth.MapPost("/password/reset", ResetPasswordAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.AccountLinksPolicy)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithName("ResetPassword")
            .WithSummary("Sets a new password with the token of a reset link and ends the user's sessions")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();

        // Anonymous: the sign-in form needs it before anybody is signed in.
        auth.MapGet("/methods", GetMethodsAsync)
            .AllowAnonymous()
            .WithName("GetLoginMethods")
            .WithSummary("Sign-in methods enabled for the tenant (password, email-otp)")
            .Produces<LoginMethodsResponse>();

        // Anonymous: the activation, reset and expired-password forms show the rules.
        auth.MapGet("/password-policy", GetPasswordPolicyAsync)
            .AllowAnonymous()
            .WithName("GetPasswordPolicy")
            .WithSummary("The password rules of the tenant")
            .Produces<PasswordPolicyResponse>();

        // Anonymous: the first step of the e-mail code sign-in.
        auth.MapPost("/otp", RequestOtpAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.AccountLinksPolicy)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .WithName("RequestLoginOtp")
            .WithSummary("E-mails a one-time sign-in code when the method is enabled (always 202 for any user name)")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem();

        // Anonymous: the user cannot sign in until the expired password is changed.
        auth.MapPost("/password/change", ChangeExpiredPasswordAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.SignInPolicy)
            .WithName("ChangeExpiredPassword")
            .WithSummary("Changes an expired password with the current one and signs in")
            .Produces<TokenResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> IssueTokensAsync(
        TokenRequest request,
        HttpContext context,
        ISessionManager sessions,
        CancellationToken cancellationToken)
    {
        var client = new ClientCredentials(
            context.Request.Headers[ClientIdHeader].ToString(),
            context.Request.Headers[ClientSecretHeader].FirstOrDefault());
        if (string.IsNullOrWhiteSpace(client.ClientId))
        {
            return Errors.Identity.ClientInvalid().ToProblem();
        }

        var ipAddress = context.Connection.RemoteIpAddress?.ToString();
        var userAgent = Truncate(context.Request.Headers.UserAgent.ToString(), RefreshSession.UserAgentMaxLength);
        var result = request.GrantType switch
        {
            PasswordGrant when !string.IsNullOrEmpty(request.UserName) && !string.IsNullOrEmpty(request.Password) =>
                await sessions.SignInAsync(new PasswordSignIn(client, request.UserName, request.Password, ipAddress, userAgent), cancellationToken),
            EmailOtpGrant when !string.IsNullOrEmpty(request.UserName) && !string.IsNullOrEmpty(request.Code) =>
                await sessions.SignInWithOtpAsync(new OtpSignIn(client, request.UserName, request.Code, ipAddress, userAgent), cancellationToken),
            RefreshTokenGrant when !string.IsNullOrEmpty(request.RefreshToken) =>
                await sessions.RefreshAsync(new RefreshTokens(client, request.RefreshToken, ipAddress, userAgent), cancellationToken),
            _ => Errors.Host.ValidationFailed(new Dictionary<string, string[]>
            {
                ["grantType"] = ["validation.auth.grantType"],
            }),
        };

        return result.ToHttpResult(pair =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var expiresIn = (int)Math.Max(0, (pair.AccessTokenExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
            return TypedResults.Ok(new TokenResponse(pair.AccessToken, "Bearer", expiresIn, pair.RefreshToken));
        });
    }

    private static async Task<IResult> GetMethodsAsync(ILoginAuditQueryService audit, CancellationToken cancellationToken) =>
        TypedResults.Ok(await audit.GetMethodsAsync(cancellationToken));

    private static async Task<IResult> GetPasswordPolicyAsync(IPasswordPolicy policy, CancellationToken cancellationToken) =>
        TypedResults.Ok(await policy.GetAsync(cancellationToken));

    private static async Task<IResult> RequestOtpAsync(LoginOtpRequest request, IAccountLinkManager links, CancellationToken cancellationToken) =>
        (await links.SendLoginOtpAsync(request.UserName, cancellationToken)).ToHttpResult(() => TypedResults.Accepted((string?)null));

    private static async Task<IResult> ChangeExpiredPasswordAsync(
        ChangeExpiredPasswordRequest request, HttpContext context, ISessionManager sessions, CancellationToken cancellationToken)
    {
        var client = new ClientCredentials(
            context.Request.Headers[ClientIdHeader].ToString(),
            context.Request.Headers[ClientSecretHeader].FirstOrDefault());
        if (string.IsNullOrWhiteSpace(client.ClientId))
        {
            return Errors.Identity.ClientInvalid().ToProblem();
        }

        var result = await sessions.ChangeExpiredPasswordAsync(
            new ExpiredPasswordChange(
                client, request.UserName, request.CurrentPassword, request.NewPassword,
                context.Connection.RemoteIpAddress?.ToString(), Truncate(context.Request.Headers.UserAgent.ToString(), RefreshSession.UserAgentMaxLength)),
            cancellationToken);

        return result.ToHttpResult(pair =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var expiresIn = (int)Math.Max(0, (pair.AccessTokenExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
            return TypedResults.Ok(new TokenResponse(pair.AccessToken, "Bearer", expiresIn, pair.RefreshToken));
        });
    }

    private static async Task<IResult> LogoutAsync(
        ClaimsPrincipal user,
        ISessionManager sessions,
        IAccessTokenDenyList denyList,
        ITenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        // The token itself is denied at once; the session end denies every other access token of the session.
        if (user.FindFirstValue(TokenClaims.TokenId) is { } tokenId
            && long.TryParse(user.FindFirstValue("exp"), out var exp))
        {
            await denyList.DenyTokenAsync(tenantContext.Tenant.Slug, tokenId, DateTimeOffset.FromUnixTimeSeconds(exp), cancellationToken);
        }

        if (!Guid.TryParse(user.FindFirstValue(TokenClaims.Session), out var sessionId))
        {
            return TypedResults.NoContent();
        }

        var result = await sessions.EndSessionAsync(sessionId, SessionEndReason.Logout, cancellationToken);
        return result.IsSuccess || result.Error!.Code == EventCodes.Identity.RefreshTokenInvalid
            ? TypedResults.NoContent()
            : result.Error.ToProblem();
    }

    private static async Task<IResult> ActivateAsync(
        ActivateAccountRequest request, IAccountLinkManager links, CancellationToken cancellationToken) =>
        (await links.ActivateAsync(request.Token, request.Password, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<Accepted> ForgotPasswordAsync(
        ForgotPasswordRequest request, IAccountLinkManager links, CancellationToken cancellationToken)
    {
        _ = await links.RequestPasswordResetAsync(request.UserName, cancellationToken);
        return TypedResults.Accepted((string?)null);
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request, IAccountLinkManager links, CancellationToken cancellationToken) =>
        (await links.ResetPasswordAsync(request.Token, request.Password, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static string? Truncate(string value, int length) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= length ? value : value[..length];
}

/// <summary><c>GET /.well-known/jwks.json</c>: the public keys that validate access tokens (current and recently retired).</summary>
internal static class JwksEndpoint
{
    public static IEndpointRouteBuilder MapJwks(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/jwks.json", async ([FromServices] SigningKeyRing ring, HttpContext context, CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "public, max-age=300";
                return TypedResults.Ok(new { keys = await ring.GetPublicJwksAsync(cancellationToken) });
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
        return app;
    }
}
