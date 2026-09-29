using System.Text.RegularExpressions;

namespace Auxilia.Application.Abstractions.Settings;

/// <summary>Validation rules shared by setting definitions.</summary>
public static partial class SettingRules
{
    /// <summary>A two-letter lower-case ISO 639-1 code (e.g. <c>it</c>, <c>en</c>).</summary>
    public static bool IsLanguageCode(string value) => value is not null && LanguageCode().IsMatch(value);

    [GeneratedRegex("^[a-z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageCode();
}
