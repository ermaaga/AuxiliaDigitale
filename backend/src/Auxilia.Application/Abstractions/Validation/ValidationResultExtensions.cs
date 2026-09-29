using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using FluentValidation.Results;

namespace Auxilia.Application.Abstractions.Validation;

/// <summary>
/// FluentValidation → <see cref="Error"/> (<c>AUX-10020</c>, 400): field names in camelCase, messages as translation
/// keys. Validators set keys with <c>.WithErrorCode("validation.fiscalCode.invalid")</c>; built-in rules without an
/// explicit code become <c>validation.&lt;rule&gt;</c> (e.g. <c>NotEmptyValidator</c> → <c>validation.notEmpty</c>).
/// </summary>
public static class ValidationResultExtensions
{
    private const string ValidatorSuffix = "Validator";

    public static Error ToError(this ValidationResult validation)
    {
        ArgumentNullException.ThrowIfNull(validation);

        var errors = validation.Errors
            .GroupBy(failure => FieldName(failure.PropertyName), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(TranslationKey).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

        return Errors.Host.ValidationFailed(errors);
    }

    public static Result ToResult(this ValidationResult validation) => Result.Failure(validation.ToError());

    public static Result<TValue> ToResult<TValue>(this ValidationResult validation) => Result.Failure<TValue>(validation.ToError());

    internal static string FieldName(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(CamelCase));

    private static string TranslationKey(ValidationFailure failure)
    {
        var code = failure.ErrorCode;
        if (string.IsNullOrEmpty(code))
        {
            return "validation.invalid";
        }

        if (code.Contains('.', StringComparison.Ordinal))
        {
            return code;
        }

        var rule = code.EndsWith(ValidatorSuffix, StringComparison.Ordinal) ? code[..^ValidatorSuffix.Length] : code;
        return "validation." + CamelCase(rule);
    }

    private static string CamelCase(string value) =>
        value.Length == 0 || char.IsLower(value[0]) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}
