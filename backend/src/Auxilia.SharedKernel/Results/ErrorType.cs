namespace Auxilia.SharedKernel.Results;

/// <summary>Category of a failure; the API maps it to an HTTP status (ProblemDetails).</summary>
public enum ErrorType
{
    /// <summary>Input is invalid (400).</summary>
    Validation,

    /// <summary>The resource does not exist or is not visible to the caller (404).</summary>
    NotFound,

    /// <summary>The operation conflicts with the current state (409).</summary>
    Conflict,

    /// <summary>The caller is authenticated but not allowed (403).</summary>
    Forbidden,

    /// <summary>The caller is not authenticated (401).</summary>
    Unauthorized,

    /// <summary>Unexpected failure (500).</summary>
    Failure,

    /// <summary>The caller's version of the resource (<c>If-Match</c>) is not the current one (412).</summary>
    PreconditionFailed,

    /// <summary>The write needs the caller's version of the resource (<c>If-Match</c>) and has none (428).</summary>
    PreconditionRequired,
}
