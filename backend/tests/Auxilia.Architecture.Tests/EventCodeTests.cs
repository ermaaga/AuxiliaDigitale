using System.Reflection;
using System.Text.RegularExpressions;

using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Architecture.Tests;

/// <summary>
/// Event codes (ADR 0006, ADR 0012, skill auxilia-log-codes): ranges, global uniqueness, typed logs and
/// error factories bound to their range, no string-based logging, generated registry in sync.
/// </summary>
public sealed partial class EventCodeTests
{
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static TheoryData<string, int> ExpectedRanges() => new()
    {
        { "Host", 10000 }, { "Tenancy", 11000 }, { "Identity", 12000 }, { "Directory", 13000 },
        { "Cases", 14000 }, { "Scheduling", 15000 }, { "Documents", 16000 }, { "Requests", 17000 },
        { "Notifications", 18000 }, { "Marketing", 19000 }, { "Configuration", 20000 }, { "Localization", 21000 },
        { "Imports", 22000 }, { "Bus", 23000 }, { "Cache", 24000 }, { "Messaging", 25000 },
        { "Jobs", 26000 }, { "Audit", 27000 }, { "Runner", 28000 }, { "Security", 29000 },
    };

    [Fact]
    public void EveryEventCodesClass_DeclaresARange()
    {
        var missing = typeof(EventCodes).GetNestedTypes()
            .Where(type => type.GetCustomAttribute<EventCodeRangeAttribute>() is null)
            .Select(type => type.Name)
            .ToArray();

        missing.ShouldBeEmpty("every EventCodes.<Range> class needs [EventCodeRange]");
    }

    [Theory]
    [MemberData(nameof(ExpectedRanges))]
    public void Range_StartsWhereTheCatalogSays(string name, int start)
    {
        // Ranges are part of the public contract: never move them.
        var range = EventRegistry.Ranges().SingleOrDefault(range => range.Name == name);

        range.ShouldNotBeNull($"range {name} is missing");
        range.Start.ShouldBe(start);
    }

    [Fact]
    public void Ranges_AreAlignedAndDoNotOverlap()
    {
        var ranges = EventRegistry.Ranges();

        ranges.ShouldAllBe(range => range.Start % EventCodeRangeAttribute.Size == 0);
        ranges.Select(range => range.Start).ShouldBeUnique();
    }

    [Fact]
    public void Codes_AreGloballyUnique()
    {
        var duplicates = Codes()
            .GroupBy(code => code.Value)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(code => $"{code.Range}.{code.Name}"))}")
            .ToArray();

        duplicates.ShouldBeEmpty();
    }

    [Fact]
    public void Codes_LieInsideTheRangeOfTheirClass()
    {
        var ranges = EventRegistry.Ranges().ToDictionary(range => range.Name);

        var outside = Codes()
            .Where(code => code.Value <= ranges[code.Range].Start || code.Value > ranges[code.Range].End)
            .Select(code => $"{code.Range}.{code.Name} = {code.Value}")
            .ToArray();

        outside.ShouldBeEmpty("a code must be in (Start, Start + 999] of its EventCodes class");
    }

    [Fact]
    public void LogMethods_UseACodeOfTheirRangeWithMatchingEventName()
    {
        var codes = Codes().ToDictionary(code => code.Value);
        var violations = new List<string>();

        foreach (var method in LogMethods())
        {
            var range = method.DeclaringType!.Name;
            var attribute = method.GetCustomAttribute<LoggerMessageAttribute>();
            if (attribute is null)
            {
                violations.Add($"Log.{range}.{method.Name} has no [LoggerMessage]");
                continue;
            }

            if (!codes.TryGetValue(attribute.EventId, out var code) || code.Range != range)
            {
                violations.Add($"Log.{range}.{method.Name} uses {attribute.EventId}, not a code of EventCodes.{range}");
                continue;
            }

            if (attribute.EventName != $"{range}.{code.Name}")
            {
                violations.Add($"Log.{range}.{method.Name} EventName must be '{range}.{code.Name}'");
            }

            if (attribute.Level == LogLevel.None)
            {
                violations.Add($"Log.{range}.{method.Name} must declare its level");
            }
        }

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void LogMethods_HaveOneEntryPerCode()
    {
        // One code = one meaning: two log entries never share a code.
        var shared = LogMethods()
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(item => item.Attribute is not null)
            .GroupBy(item => item.Attribute!.EventId)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(item => item.Method.Name))}")
            .ToArray();

        shared.ShouldBeEmpty();
    }

    [Fact]
    public void ErrorFactories_UseACodeOfTheirRange()
    {
        var codes = Codes().ToDictionary(code => code.Value);
        var violations = new List<string>();

        foreach (var factory in typeof(Errors).GetNestedTypes().SelectMany(type => type.GetMethods(PublicStatic)))
        {
            var range = factory.DeclaringType!.Name;
            if (factory.ReturnType != typeof(Error))
            {
                violations.Add($"Errors.{range}.{factory.Name} must return Error");
                continue;
            }

            var error = Invoke(factory);
            if (error is null || !codes.TryGetValue(error.Code, out var code) || code.Range != range)
            {
                violations.Add($"Errors.{range}.{factory.Name} uses {error?.Code}, not a code of EventCodes.{range}");
            }
        }

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void LogAndErrorClasses_MatchAnEventCodesRange()
    {
        var ranges = EventRegistry.Ranges().Select(range => range.Name).ToHashSet();

        typeof(Log).GetNestedTypes().Select(type => type.Name).Where(name => !ranges.Contains(name)).ShouldBeEmpty();
        typeof(Errors).GetNestedTypes().Select(type => type.Name).Where(name => !ranges.Contains(name)).ShouldBeEmpty();
    }

    [Fact]
    public void ProductionCode_DoesNotLogWithStrings()
    {
        // Information and above go through Log.* ([LoggerMessage]); only Debug/Trace may use the extension methods.
        var source = Path.Combine(Solution.BackendDirectory.FullName, "src");
        var separator = Path.DirectorySeparatorChar;

        var violations = Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !path.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Line: line, Number: index + 1))
                .Where(item => StringLogCall().IsMatch(item.Line))
                .Select(item => $"{Path.GetRelativePath(source, path)}:{item.Number}"))
            .ToArray();

        violations.ShouldBeEmpty("use a Log.<Range>.<Event> [LoggerMessage] method");
    }

    [Fact]
    public void Registry_IsInSyncWithTheCatalog()
    {
        var path = Path.Combine(Solution.BackendDirectory.Parent!.FullName, "docs", "log-event-registry.md");

        File.Exists(path).ShouldBeTrue("docs/log-event-registry.md is missing");
        File.ReadAllText(path).ReplaceLineEndings("\n").ShouldBe(
            EventRegistry.RenderMarkdown(),
            "regenerate it: dotnet run --project backend/src/Auxilia.MigrationRunner -- diagnostics registry --output docs/log-event-registry.md");
    }

    [Fact]
    public void Registry_ListsEveryCodeWithItsLogAndErrorType()
    {
        var entries = EventRegistry.Entries();

        entries.Select(entry => entry.Code).ShouldBe(Codes().Select(code => code.Value).Order());
        var unhandled = entries.Single(entry => entry.Code == EventCodes.Host.UnhandledException);
        unhandled.Level.ShouldBe(LogLevel.Error);
        unhandled.ErrorTypes.ShouldBe([ErrorType.Failure]);
    }

    private static IEnumerable<(string Range, string Name, int Value)> Codes() =>
        typeof(EventCodes).GetNestedTypes()
            .SelectMany(type => type.GetFields(PublicStatic)
                .Where(field => field.IsLiteral && field.FieldType == typeof(int))
                .Select(field => (type.Name, field.Name, (int)field.GetRawConstantValue()!)));

    private static IEnumerable<MethodInfo> LogMethods() =>
        typeof(Log).GetNestedTypes().SelectMany(type => type.GetMethods(PublicStatic));

    private static Error? Invoke(MethodInfo factory)
    {
        var arguments = factory.GetParameters()
            .Select(parameter => parameter.ParameterType == typeof(string)
                ? string.Empty
                : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null)
            .ToArray();

        return factory.Invoke(null, arguments) as Error;
    }

    [GeneratedRegex(@"\bLog(Information|Warning|Error|Critical)\s*\(|\.Log\s*\(\s*LogLevel\.(Information|Warning|Error|Critical)")]
    private static partial Regex StringLogCall();
}
