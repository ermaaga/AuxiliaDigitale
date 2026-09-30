using System.Diagnostics;

using Auxilia.Diagnostics;

namespace Auxilia.Api.Infrastructure;

/// <summary>
/// Every error response is RFC 9457 ProblemDetails with <c>errorCode</c> (<c>AUX-NNNNN</c>) and <c>traceId</c>
/// (skill auxilia-api-contract, ADR 0012). Responses produced by the framework (unknown route, wrong method,
/// unreadable request, unhandled exception) receive the matching Host code.
/// </summary>
internal static class ProblemDetailsSetup
{
    public const string ErrorTypeBaseUri = "https://docs.auxilia.app/errors/";

    public const string ErrorCodeKey = "errorCode";

    public const string TraceIdKey = "traceId";

    private static readonly Dictionary<int, (int Code, string Title)> FrameworkErrors = new()
    {
        [StatusCodes.Status400BadRequest] = (EventCodes.Host.RequestInvalid, "The request could not be read"),
        [StatusCodes.Status401Unauthorized] = (EventCodes.Host.AuthenticationRequired, "A valid access token is required"),
        [StatusCodes.Status403Forbidden] = (EventCodes.Host.AccessDenied, "The caller is not allowed to use this endpoint"),
        [StatusCodes.Status404NotFound] = (EventCodes.Host.EndpointNotFound, "No endpoint matches the request"),
        [StatusCodes.Status405MethodNotAllowed] = (EventCodes.Host.MethodNotAllowed, "The endpoint does not support this HTTP method"),
        [StatusCodes.Status429TooManyRequests] = (EventCodes.Host.TooManyRequests, "Too many requests, retry later"),
        [StatusCodes.Status500InternalServerError] = (EventCodes.Host.UnhandledException, "An unexpected error occurred"),
    };

    public static IServiceCollection AddAuxiliaProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = Customize);

    public static string ErrorCode(int code) => $"AUX-{code}";

    public static string ErrorTypeUri(int code) => ErrorTypeBaseUri + ErrorCode(code);

    private static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        problem.Extensions[TraceIdKey] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        problem.Extensions.Remove("requestId");

        if (!problem.Extensions.ContainsKey(ErrorCodeKey)
            && problem.Status is { } status
            && FrameworkErrors.TryGetValue(status, out var error))
        {
            problem.Extensions[ErrorCodeKey] = ErrorCode(error.Code);
            problem.Type = ErrorTypeUri(error.Code);
            problem.Title = error.Title;
            problem.Detail = null;
        }
    }
}
