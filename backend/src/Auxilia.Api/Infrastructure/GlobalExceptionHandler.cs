using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Auxilia.Api.Infrastructure;

/// <summary>
/// Last line of ADR 0012: the client cancelling is not an error; known technical exceptions get their own code;
/// anything else is logged once as <c>AUX-10001</c> and answered with a generic 500 (no exception details).
/// Exceptions inside Manager operations are logged and classified by <c>IOperationRunner</c> first.
/// </summary>
internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    /// <summary>Non-standard "client closed request" status, only visible in logs and traces.</summary>
    public const int ClientClosedRequest = 499;

    private readonly IProblemDetailsService problemDetails;
    private readonly ILogger<GlobalExceptionHandler> logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger)
    {
        this.problemDetails = problemDetails;
        this.logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var request = httpContext.Request;
        var method = request.Method;
        var path = request.Path.Value ?? string.Empty;

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            Log.Host.RequestCancelled(logger, method, path);
            httpContext.Response.StatusCode = ClientClosedRequest;
            return true;
        }

        if (exception is BadHttpRequestException badRequest)
        {
            // Unreadable body/route/query (thrown in Development; elsewhere the framework answers directly).
            return await WriteAsync(httpContext, exception, badRequest.StatusCode, code: null, title: null);
        }

        // An exception from IOperationRunner already has its outcome log: do not log it twice (ADR 0012).
        var alreadyLogged = ExceptionLogging.IsLogged(exception);

        if (IsTimeout(exception))
        {
            if (!alreadyLogged)
            {
                Log.Host.DatabaseTimeout(logger, exception, $"{method} {path}");
            }

            var timeout = Errors.Host.DatabaseTimeout();
            return await WriteAsync(httpContext, exception, StatusCodes.Status503ServiceUnavailable, timeout.Code, timeout.Description);
        }

        if (!alreadyLogged)
        {
            Log.Host.UnhandledException(logger, exception, method, path);
        }

        var unexpected = Errors.Host.Unexpected();
        return await WriteAsync(httpContext, exception, StatusCodes.Status500InternalServerError, unexpected.Code, unexpected.Description);
    }

    private static bool IsTimeout(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    private ValueTask<bool> WriteAsync(HttpContext httpContext, Exception exception, int status, int? code, string? title)
    {
        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails { Status = status };
        if (code is { } errorCode)
        {
            problem.Title = title;
            problem.Type = ProblemDetailsSetup.ErrorTypeUri(errorCode);
            problem.Extensions[ProblemDetailsSetup.ErrorCodeKey] = ProblemDetailsSetup.ErrorCode(errorCode);
        }

        return problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
