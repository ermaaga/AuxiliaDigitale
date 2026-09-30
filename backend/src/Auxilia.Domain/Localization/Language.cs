using System.Text.RegularExpressions;

using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Localization;

/// <summary>
/// A language of the tenant (<c>localization.languages</c>, F24): code (<c>en</c>, <c>it</c>, <c>pt-BR</c>) and native
/// name. Languages are added by data-migrations; only active languages are served to clients.
/// </summary>
public sealed partial class Language : AggregateRoot<Guid>, IAuditable
{
    public const int CodeMaxLength = 10;
    public const int NameMaxLength = 50;

    /// <summary>The last fallback of every bundle and the language every shipped key has.</summary>
    public const string English = "en";

    public Language(Guid id, string code, string name)
        : base(id)
    {
        if (!IsValidCode(code))
        {
            throw new ArgumentException($"'{code}' is not a language code.", nameof(code));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, NameMaxLength);

        Code = code;
        Name = name;
        IsActive = true;
    }

    private Language()
    {
        Code = Name = string.Empty;
    }

    public string Code { get; private set; }

    /// <summary>Name in the language itself (<c>English</c>, <c>Italiano</c>), as shown by a language switcher.</summary>
    public string Name { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>ISO 639 code, optionally with an ISO 3166 region: <c>it</c>, <c>pt-BR</c>.</summary>
    public static bool IsValidCode(string? code) => code is not null && CodePattern().IsMatch(code);

    public void SetActive(bool isActive) => IsActive = isActive;

    [GeneratedRegex("^[a-z]{2,3}(-[A-Z]{2})?$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
