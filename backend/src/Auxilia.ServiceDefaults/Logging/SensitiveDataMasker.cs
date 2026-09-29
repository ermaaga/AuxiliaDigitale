using System.Text.RegularExpressions;

using Serilog.Core;
using Serilog.Events;

namespace Auxilia.ServiceDefaults.Logging;

/// <summary>
/// Masks sensitive values before any sink sees them (skill auxilia-log-codes): properties named like passwords,
/// secrets, tokens, keys, cookies or connection strings become <c>***</c>; Italian fiscal codes keep only the first
/// three and the last character (<c>RSS***…***X</c>), whatever the property name. Nested structures are masked too.
/// </summary>
public sealed partial class SensitiveDataMasker : ILogEventEnricher
{
    public const string Redacted = "***";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var masked = logEvent.Properties
            .Select(property => (property.Key, Original: property.Value, Masked: Mask(property.Key, property.Value)))
            .Where(item => !ReferenceEquals(item.Original, item.Masked))
            .ToArray();

        foreach (var (name, _, value) in masked)
        {
            logEvent.AddOrUpdateProperty(new LogEventProperty(name, value));
        }
    }

    public static string MaskFiscalCode(string fiscalCode)
    {
        ArgumentNullException.ThrowIfNull(fiscalCode);
        return fiscalCode.Length < 4 ? Redacted : $"{fiscalCode[..3]}***…***{fiscalCode[^1]}";
    }

    private static LogEventPropertyValue Mask(string? name, LogEventPropertyValue value)
    {
        if (name is not null && SecretName().IsMatch(name))
        {
            return new ScalarValue(Redacted);
        }

        switch (value)
        {
            case ScalarValue { Value: string text } when FiscalCode().IsMatch(text):
                return new ScalarValue(MaskFiscalCode(text));

            case StructureValue structure:
            {
                var properties = structure.Properties
                    .Select(property => (Property: property, Masked: Mask(property.Name, property.Value)))
                    .ToArray();
                return properties.Any(item => !ReferenceEquals(item.Property.Value, item.Masked))
                    ? new StructureValue(properties.Select(item => new LogEventProperty(item.Property.Name, item.Masked)), structure.TypeTag)
                    : value;
            }

            case DictionaryValue dictionary:
            {
                var elements = dictionary.Elements
                    .Select(element => (element.Key, Original: element.Value, Masked: Mask(element.Key.Value as string, element.Value)))
                    .ToArray();
                return elements.Any(item => !ReferenceEquals(item.Original, item.Masked))
                    ? new DictionaryValue(elements.Select(item => new KeyValuePair<ScalarValue, LogEventPropertyValue>(item.Key, item.Masked)))
                    : value;
            }

            case SequenceValue sequence:
            {
                var elements = sequence.Elements.Select(element => (Original: element, Masked: Mask(null, element))).ToArray();
                return elements.Any(item => !ReferenceEquals(item.Original, item.Masked))
                    ? new SequenceValue(elements.Select(item => item.Masked))
                    : value;
            }

            default:
                return value;
        }
    }

    [GeneratedRegex("password|passwd|pwd|secret|token|api[-_]?key|authorization|cookie|connectionstring|credential",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretName();

    // Italian fiscal code, including omocodia substitutions (digits replaced by L M N P Q R S T U V).
    [GeneratedRegex("^[A-Z]{6}[0-9LMNPQRSTUV]{2}[ABCDEHLMPRST][0-9LMNPQRSTUV]{2}[A-Z][0-9LMNPQRSTUV]{3}[A-Z]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FiscalCode();
}
