using System.Collections;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Execution;

/// <summary>
/// Outcome log of <see cref="OperationRunner"/>. Its event code varies per operation (success code from the
/// <c>Operations</c> catalog, or the error code), so it cannot be a <c>[LoggerMessage]</c> constant: it calls
/// <see cref="ILogger.Log{TState}"/> with the same structured state the source generator produces. Every code used
/// here is declared in <c>EventCodes</c> and verified by the registry tests.
/// </summary>
internal static class OperationLog
{
    public const string SucceededTemplate = "Operation {Operation} succeeded in {ElapsedMs} ms";

    public const string FailedTemplate = "Operation {Operation} failed with {ErrorCode}: {ErrorDescription}";

    public const string ErrorTemplate = "Operation {Operation} failed unexpectedly with {ErrorCode}";

    public static void Succeeded(ILogger logger, EventId eventId, string operation, double elapsedMs) =>
        Write(logger, LogLevel.Information, eventId, null, SucceededTemplate,
            ("Operation", operation), ("ElapsedMs", Math.Round(elapsedMs, 1)));

    public static void Failed(ILogger logger, EventId eventId, string operation, string errorCode, string errorDescription, Exception? exception) =>
        Write(logger, LogLevel.Warning, eventId, exception, FailedTemplate,
            ("Operation", operation), ("ErrorCode", errorCode), ("ErrorDescription", errorDescription));

    public static void Error(ILogger logger, EventId eventId, string operation, string errorCode, Exception exception) =>
        Write(logger, LogLevel.Error, eventId, exception, ErrorTemplate, ("Operation", operation), ("ErrorCode", errorCode));

    private static void Write(
        ILogger logger, LogLevel level, EventId eventId, Exception? exception, string template, params (string Name, object? Value)[] values)
    {
        if (!logger.IsEnabled(level))
        {
            return;
        }

        var state = new LogState(template, values);
        logger.Log(level, eventId, state, exception, static (value, _) => value.ToString());
    }

    private sealed class LogState(string template, (string Name, object? Value)[] values) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => values.Length + 1;

        public KeyValuePair<string, object?> this[int index] => index == values.Length
            ? new("{OriginalFormat}", template)
            : new(values[index].Name, values[index].Value);

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() =>
            values.Aggregate(template, (text, value) => text.Replace("{" + value.Name + "}", Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }
}
