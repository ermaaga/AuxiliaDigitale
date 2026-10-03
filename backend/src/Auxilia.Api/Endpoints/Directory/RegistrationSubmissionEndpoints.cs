using Auxilia.Api.Endpoints.Identity;
using Auxilia.Api.Infrastructure;
using Auxilia.Api.RateLimiting;
using Auxilia.Api.Tenancy;
using Auxilia.Application.Abstractions.Captcha;
using Auxilia.Application.Directory;
using Auxilia.Contracts.Directory;

namespace Auxilia.Api.Endpoints.Directory;

/// <summary>
/// External registration (F02, API only D-14): called by a registered client application (<c>X-Client-Id</c>, plus
/// <c>X-Client-Secret</c> for confidential clients) for the tenant of the host or <c>X-Tenant</c>. Anonymous because
/// the applicant has no account yet; protected by the client application, its captcha and a per-IP rate limit. Outside
/// the module group (which needs a signed-in role): the manager answers <c>AUX-13037</c> when the tenant does not
/// accept registrations.
/// </summary>
internal sealed class RegistrationSubmissionEndpoints : IApiEndpoints
{
    public void Map(RouteGroupBuilder api)
    {
        var registrations = api.MapGroup("/registrations").WithTags("Registrations").RequireTenant();

        // Anonymous: the external registration form loads it before anybody has an account.
        registrations.MapGet("/captcha", CaptchaAsync)
            .AllowAnonymous()
            .WithName("GetRegistrationCaptcha")
            .WithSummary("The captcha challenge the client application must solve before sending a registration (403 when registrations are closed)")
            .Produces<CaptchaChallengeResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        // Anonymous: the applicant has no account yet.
        registrations.MapPost("/", SubmitAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingSetup.RegistrationPolicy)
            .WithName("SubmitRegistration")
            .WithSummary("Sends a registration request for staff review (one pending request per e-mail)")
            .Produces<RegistrationSubmittedResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> CaptchaAsync(HttpContext context, IRegistrationManager registrations, CancellationToken cancellationToken) =>
        (await registrations.CreateCaptchaAsync(Caller(context), cancellationToken)).ToHttpResult(challenge =>
        {
            // A challenge is valid once: never cached.
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new CaptchaChallengeResponse(challenge.Provider, ToResponse(challenge.Altcha)));
        });

    private static async Task<IResult> SubmitAsync(
        SubmitRegistrationRequest request, HttpContext context, IRegistrationManager registrations, CancellationToken cancellationToken) =>
        (await registrations.SubmitAsync(request, Caller(context), cancellationToken)).ToHttpResult(submitted => TypedResults.Accepted((string?)null, submitted));

    private static RegistrationCaller Caller(HttpContext context) =>
        new(context.Request.Headers[AuthEndpoints.ClientIdHeader].FirstOrDefault(), context.Request.Headers[AuthEndpoints.ClientSecretHeader].FirstOrDefault());

    private static AltchaChallengeResponse? ToResponse(AltchaChallenge? challenge) =>
        challenge is null
            ? null
            : new AltchaChallengeResponse(challenge.Algorithm, challenge.Challenge, challenge.Salt, challenge.Signature, challenge.MaxNumber);
}
