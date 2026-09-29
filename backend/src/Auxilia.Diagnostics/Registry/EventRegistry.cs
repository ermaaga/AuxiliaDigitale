using System.Globalization;
using System.Reflection;
using System.Text;

using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

/// <summary>A range of event codes owned by a nested class of <see cref="EventCodes"/>.</summary>
public sealed record EventCodeRange(string Name, string Owner, int Start, int End);

/// <summary>One event code with what uses it: the <see cref="Log"/> entry and/or the <see cref="Errors"/> factories.</summary>
public sealed record EventRegistryEntry(
    int Code,
    string Range,
    string Name,
    LogLevel? Level,
    string? Message,
    IReadOnlyList<ErrorType> ErrorTypes,
    string? Operation,
    string? RetiredReason)
{
    public string DisplayCode => $"AUX-{Code}";
}

/// <summary>
/// Builds the event-code registry by reflection over <see cref="EventCodes"/>, <see cref="Log"/> and <see cref="Errors"/>,
/// and renders <c>docs/log-event-registry.md</c> (<c>auxctl diagnostics registry</c>). The file is never edited by hand.
/// </summary>
public static class EventRegistry
{
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Lazy<IReadOnlyDictionary<int, string>> EventNames = new(() =>
        Entries().ToDictionary(entry => entry.Code, entry => $"{entry.Range}.{entry.Name}"));

    /// <summary>The operations of the <see cref="Operations"/> catalog.</summary>
    public static IReadOnlyList<OperationDescriptor> OperationCatalog() =>
        typeof(Operations).GetNestedTypes()
            .SelectMany(type => type.GetFields(PublicStatic))
            .Where(field => field.FieldType == typeof(OperationDescriptor))
            .Select(field => (OperationDescriptor)field.GetValue(null)!)
            .ToArray();

    /// <summary>The event name <c>&lt;Range&gt;.&lt;CodeName&gt;</c> of a declared code, or null.</summary>
    public static string? EventNameOf(int code) => EventNames.Value.GetValueOrDefault(code);

    public static IReadOnlyList<EventCodeRange> Ranges() =>
        typeof(EventCodes).GetNestedTypes()
            .Select(type => (Type: type, Range: type.GetCustomAttribute<EventCodeRangeAttribute>()))
            .Where(item => item.Range is not null)
            .Select(item => new EventCodeRange(item.Type.Name, item.Range!.Owner, item.Range.Start, item.Range.End))
            .OrderBy(range => range.Start)
            .ToArray();

    public static IReadOnlyList<EventRegistryEntry> Entries()
    {
        var logs = typeof(Log).GetNestedTypes()
            .SelectMany(type => type.GetMethods(PublicStatic))
            .Select(method => method.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .ToDictionary(attribute => attribute.EventId);

        var errorTypes = typeof(Errors).GetNestedTypes()
            .SelectMany(type => type.GetMethods(PublicStatic))
            .Where(method => method.ReturnType == typeof(Error))
            .Select(CreateSample)
            .GroupBy(error => error.Code)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Type).Distinct().Order().ToArray());

        var operations = OperationCatalog()
            .GroupBy(operation => operation.SuccessCode)
            .ToDictionary(group => group.Key, group => string.Join(", ", group.Select(operation => operation.Name)));

        return typeof(EventCodes).GetNestedTypes()
            .SelectMany(type => type.GetFields(PublicStatic)
                .Where(field => field.IsLiteral && field.FieldType == typeof(int))
                .Select(field => (Range: type.Name, Field: field, Code: (int)field.GetRawConstantValue()!)))
            .Select(item =>
            {
                var log = logs.GetValueOrDefault(item.Code);
                return new EventRegistryEntry(
                    item.Code,
                    item.Range,
                    item.Field.Name,
                    log?.Level,
                    log?.Message,
                    errorTypes.GetValueOrDefault(item.Code) ?? [],
                    operations.GetValueOrDefault(item.Code),
                    item.Field.GetCustomAttribute<ObsoleteAttribute>()?.Message);
            })
            .OrderBy(entry => entry.Code)
            .ToArray();
    }

    public static string RenderMarkdown()
    {
        var entries = Entries();
        var builder = new StringBuilder();

        builder.AppendLine("# Log event registry");
        builder.AppendLine();
        builder.AppendLine("<!-- Generated from Auxilia.Diagnostics. Do not edit by hand:");
        builder.AppendLine("     dotnet run --project backend/src/Auxilia.MigrationRunner -- diagnostics registry --output docs/log-event-registry.md -->");
        builder.AppendLine();
        builder.AppendLine("Every code is shown as `AUX-NNNNN` in logs, in the `errorCode` of API responses and in the UI (ADR 0006, ADR 0012).");
        builder.AppendLine("A new code is the current max of its range + 1; codes are never reused or renumbered.");
        builder.AppendLine();
        builder.AppendLine("## Ranges");
        builder.AppendLine();
        builder.AppendLine("| Range | Name | Owner | Codes | Next code |");
        builder.AppendLine("|---|---|---|---|---|");
        foreach (var range in Ranges())
        {
            var codes = entries.Where(entry => entry.Code >= range.Start && entry.Code <= range.End).Select(entry => entry.Code).ToArray();
            var next = codes.Length == 0 ? range.Start + 1 : codes.Max() + 1;
            builder.AppendLine(Invariant($"| {range.Start}–{range.End} | {range.Name} | {Escape(range.Owner)} | {codes.Length} | {next} |"));
        }

        builder.AppendLine();
        builder.AppendLine("## Codes");
        builder.AppendLine();
        builder.AppendLine("| Code | Event name | Log level | Error type | Operation | Message |");
        builder.AppendLine("|---|---|---|---|---|---|");
        foreach (var entry in entries)
        {
            var name = $"{entry.Range}.{entry.Name}";
            if (entry.RetiredReason is not null)
            {
                name += $" (retired: {entry.RetiredReason})";
            }

            var level = entry.Level?.ToString() ?? "–";
            var errorType = entry.ErrorTypes.Count == 0 ? "–" : string.Join(", ", entry.ErrorTypes);
            var operation = entry.Operation is null ? "–" : $"{entry.Operation} (success)";
            builder.AppendLine(Invariant($"| {entry.DisplayCode} | {Escape(name)} | {level} | {errorType} | {operation} | {Escape(entry.Message ?? "–")} |"));
        }

        return builder.ToString().ReplaceLineEndings("\n");
    }

    private static Error CreateSample(MethodInfo factory)
    {
        // Factories are pure: calling them with default arguments reveals their code and type.
        var arguments = factory.GetParameters()
            .Select(parameter => parameter.ParameterType == typeof(string)
                ? string.Empty
                : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null)
            .ToArray();

        return (Error)factory.Invoke(null, arguments)!;
    }

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
