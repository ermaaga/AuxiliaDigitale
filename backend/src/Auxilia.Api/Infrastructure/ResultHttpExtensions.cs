using Auxilia.SharedKernel.Results;

namespace Auxilia.Api.Infrastructure;

/// <summary>Maps <see cref="Result"/> to HTTP: success to the given response, failure to ProblemDetails (ADR 0012).</summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttpResult(this Result result, Func<IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Match(onSuccess, ToProblem);
    }

    public static IResult ToHttpResult<TValue>(this Result<TValue> result, Func<TValue, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Match(onSuccess, ToProblem);
    }

    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var extensions = new Dictionary<string, object?> { [ProblemDetailsSetup.ErrorCodeKey] = error.DisplayCode };
        var type = ProblemDetailsSetup.ErrorTypeUri(error.Code);

        return error.Type == ErrorType.Validation
            ? TypedResults.ValidationProblem(error.ValidationErrors, title: error.Description, type: type, extensions: extensions)
            : TypedResults.Problem(title: error.Description, statusCode: StatusCode(error.Type), type: type, extensions: extensions);
    }

    public static int StatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.PreconditionFailed => StatusCodes.Status412PreconditionFailed,
        ErrorType.PreconditionRequired => StatusCodes.Status428PreconditionRequired,
        ErrorType.Failure => StatusCodes.Status500InternalServerError,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown error type"),
    };
}
