using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Auxilia.Application.Abstractions.Settings;

/// <summary>
/// A typed configuration setting (ARCHITECTURE §7.1): no free keys, every value stored anywhere belongs to a
/// definition registered by its module. Values are stored as JSON; the key is <c>module.name[.name]</c> in camelCase
/// (e.g. <c>cases.expiry.expiringDays</c>) and its description is the translation key <see cref="DescriptionKey"/>.
/// </summary>
public abstract partial class SettingDefinition
{
    public const int KeyMaxLength = 150;

    /// <summary>JSON used to store values: web defaults (camelCase) and enums as strings.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter() },
    };

    private protected SettingDefinition(string key, string module, SettingScope scopes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        if (key.Length > KeyMaxLength || !KeyPattern().IsMatch(key))
        {
            throw new ArgumentException($"Setting key '{key}' must be dotted camelCase segments (e.g. 'cases.expiry.expiringDays').", nameof(key));
        }

        if (scopes == SettingScope.None)
        {
            throw new ArgumentException($"Setting '{key}' must allow at least one level.", nameof(scopes));
        }

        Key = key;
        Module = module;
        Scopes = scopes;
    }

    public string Key { get; }

    /// <summary>Owning module (e.g. <c>Cases</c>); the System editor groups settings by module.</summary>
    public string Module { get; }

    public SettingScope Scopes { get; }

    public string DescriptionKey => $"settings.{Key}.description";

    public abstract bool IsSecret { get; }

    public abstract Type ValueType { get; }

    /// <summary>The code default as JSON (level 1); <c>null</c> when the setting has none (secrets).</summary>
    public abstract string? DefaultJson { get; }

    /// <summary>The only values allowed (enum names, or a fixed list), for a select in the System editor; otherwise <c>null</c>.</summary>
    public virtual IReadOnlyList<string>? Choices => null;

    /// <summary>Whether a value can be stored at <paramref name="level"/> (a single level).</summary>
    public bool Allows(SettingScope level) => level != SettingScope.None && (Scopes & level) == level;

    /// <summary>
    /// Checks a candidate value and returns the JSON to store (canonical form). Secret definitions return the plain
    /// value only as a JSON string; the caller protects it before storing.
    /// </summary>
    public abstract bool TryNormalize(JsonElement value, [NotNullWhen(true)] out string? json);

    [GeneratedRegex("^[a-z][a-zA-Z0-9]*(\\.[a-z][a-zA-Z0-9]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}

/// <summary>A non-secret setting with a code default and an optional validation rule.</summary>
public sealed class SettingDefinition<T> : SettingDefinition
    where T : notnull
{
    private readonly Func<T, bool>? isValid;

    /// <param name="choices">The only values allowed (shown as a select); enums list their names on their own.</param>
    public SettingDefinition(
        string key, string module, T defaultValue, SettingScope scopes = SettingScope.PlatformAndTenant, Func<T, bool>? isValid = null, IReadOnlyList<T>? choices = null)
        : base(key, module, scopes)
    {
        ArgumentNullException.ThrowIfNull(defaultValue);
        if (choices is not null)
        {
            var allowed = choices;
            var rule = isValid;
            isValid = value => allowed.Contains(value) && (rule is null || rule(value));
        }

        if (isValid is not null && !isValid(defaultValue))
        {
            throw new ArgumentException($"The default of setting '{key}' fails its own validation.", nameof(defaultValue));
        }

        Default = defaultValue;
        this.isValid = isValid;
        DefaultJson = Write(defaultValue);
        Choices = typeof(T).IsEnum
            ? Enum.GetNames(typeof(T))
            : choices?.Select(choice => JsonSerializer.Deserialize<JsonElement>(Write(choice)).ToString()).ToArray();
    }

    public override IReadOnlyList<string>? Choices { get; }

    public T Default { get; }

    public override bool IsSecret => false;

    public override Type ValueType => typeof(T);

    public override string DefaultJson { get; }

    /// <summary>Reads a stored value; <c>false</c> when it has the wrong type or fails the validation.</summary>
    public bool TryRead(string json, [MaybeNullWhen(false)] out T value)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            value = JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            value = default;
            return false;
        }

        return value is not null && (isValid is null || isValid(value));
    }

    public string Write(T value) => JsonSerializer.Serialize(value, JsonOptions);

    public override bool TryNormalize(JsonElement value, [NotNullWhen(true)] out string? json)
    {
        if (TryRead(value.GetRawText(), out var typed))
        {
            json = Write(typed);
            return true;
        }

        json = null;
        return false;
    }
}

/// <summary>
/// A secret setting (credentials, tokens): a string stored protected with Data Protection, kept protected in the
/// cache and decrypted only by <see cref="ISettingsProvider.GetSecretAsync"/> at the point of use. Never a user-level
/// setting and never returned by the API.
/// </summary>
public sealed class SecretSettingDefinition : SettingDefinition
{
    public SecretSettingDefinition(string key, string module, SettingScope scopes = SettingScope.PlatformAndTenant)
        : base(key, module, scopes)
    {
        if (scopes.HasFlag(SettingScope.User))
        {
            throw new ArgumentException($"Secret setting '{key}' cannot be a user setting.", nameof(scopes));
        }
    }

    public override bool IsSecret => true;

    public override Type ValueType => typeof(string);

    public override string? DefaultJson => null;

    public override bool TryNormalize(JsonElement value, [NotNullWhen(true)] out string? json)
    {
        if (value.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(value.GetString()))
        {
            json = value.GetRawText();
            return true;
        }

        json = null;
        return false;
    }
}
