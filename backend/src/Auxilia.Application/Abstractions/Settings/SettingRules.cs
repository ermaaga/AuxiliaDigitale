using System.Text.RegularExpressions;

namespace Auxilia.Application.Abstractions.Settings;

/// <summary>Validation rules shared by setting definitions.</summary>
public static partial class SettingRules
{
    /// <summary>A two-letter lower-case ISO 639-1 code (e.g. <c>it</c>, <c>en</c>).</summary>
    public static bool IsLanguageCode(string value) => value is not null && LanguageCode().IsMatch(value);

    /// <summary>A CSS hex colour <c>#rgb</c> or <c>#rrggbb</c> (branding; parsed again by the UI before use).</summary>
    public static bool IsHexColor(string value) => value is not null && HexColor().IsMatch(value);

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();

    [GeneratedRegex("^[a-z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageCode();
}
