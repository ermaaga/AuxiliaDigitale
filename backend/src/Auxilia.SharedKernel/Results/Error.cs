namespace Auxilia.SharedKernel.Results;

/// <summary>
/// A business or technical failure. <see cref="Code"/> is the unique event code (shown as <c>AUX-NNNNN</c>)
/// shared with the log entry and the API response (ADR 0006). <see cref="Description"/> is technical English
/// text for logs and developers; user-facing text is resolved from the code by the client.
/// </summary>
public sealed record Error
{
    private static readonly IReadOnlyDictionary<string, string[]> NoValidationErrors =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    private Error(int code, ErrorType type, string description, IReadOnlyDictionary<string, string[]>? validationErrors)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Code = code;
        Type = type;
        Description = description;
        ValidationErrors = validationErrors ?? NoValidationErrors;
    }

    public int Code { get; }

    public ErrorType Type { get; }

    public string Description { get; }

    /// <summary>Field name → validation messages (translation keys); empty unless <see cref="Type"/> is Validation.</summary>
    public IReadOnlyDictionary<string, string[]> ValidationErrors { get; }

    /// <summary>The code in the format shown to users and written in logs, e.g. <c>AUX-14004</c>.</summary>
    public string DisplayCode => $"AUX-{Code}";

    public static Error Validation(int code, string description, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(code, ErrorType.Validation, description, errors);

    public static Error NotFound(int code, string description) => new(code, ErrorType.NotFound, description, null);

    public static Error Conflict(int code, string description) => new(code, ErrorType.Conflict, description, null);

    public static Error Forbidden(int code, string description) => new(code, ErrorType.Forbidden, description, null);

    public static Error Unauthorized(int code, string description) => new(code, ErrorType.Unauthorized, description, null);

    public static Error Failure(int code, string description) => new(code, ErrorType.Failure, description, null);

    public override string ToString() => $"{DisplayCode} ({Type}): {Description}";
}
