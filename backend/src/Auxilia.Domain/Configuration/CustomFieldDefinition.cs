using System.Text.RegularExpressions;

using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Configuration;

/// <summary>Value type of a custom field (F20; the legacy had text, number, date, boolean).</summary>
public enum CustomFieldType
{
    Text,
    Number,
    Date,
    Boolean,
    Select,
    MultiSelect,
}

/// <summary>What the System edits on a custom field; the entity, the key and the type never change after creation.</summary>
public sealed record CustomFieldSpec(
    string Label,
    CustomFieldType Type,
    IReadOnlyList<string>? Options,
    bool IsRequired,
    string? GroupName,
    string? BadgeColor,
    bool VisibleOnGrid,
    bool DashboardCounter,
    int Order);

/// <summary>
/// A custom field of an entity (<c>configuration.custom_field_definitions</c>, F20, D-18): the records keep the values in
/// <c>custom_fields jsonb</c> under <see cref="Key"/>. Grouped fields share one grid column, booleans as coloured badges
/// (legacy <c>GroupName</c>/<c>BadgeColor</c>); a boolean can be a dashboard counter (Q41: CAF, PATRONATO).
/// </summary>
public sealed partial class CustomFieldDefinition : AggregateRoot<Guid>, IAuditable
{
    public const int EntityTypeMaxLength = 50;
    public const int KeyMaxLength = 50;
    public const int LabelMaxLength = 100;
    public const int GroupNameMaxLength = 50;
    public const int OptionMaxLength = 100;
    public const int MaxOptions = 50;
    public const int MaxOrder = 9999;

    private CustomFieldDefinition(Guid id, string entityType, string key, CustomFieldType type)
        : base(id)
    {
        EntityType = entityType;
        Key = key;
        Type = type;
        Label = string.Empty;
        Options = [];
    }

    private CustomFieldDefinition()
    {
        EntityType = Key = Label = string.Empty;
        Options = [];
    }

    public string EntityType { get; private set; }

    /// <summary>Name of the value in <c>custom_fields</c> (legacy <c>PropertyName</c>, e.g. <c>CAF</c>).</summary>
    public string Key { get; private set; }

    public string Label { get; private set; }

    public CustomFieldType Type { get; private set; }

    /// <summary>The allowed values of <see cref="CustomFieldType.Select"/> and <see cref="CustomFieldType.MultiSelect"/>.</summary>
    public List<string> Options { get; private set; }

    public bool IsRequired { get; private set; }

    public string? GroupName { get; private set; }

    /// <summary>Hex colour of the badge; only for grouped fields.</summary>
    public string? BadgeColor { get; private set; }

    public bool VisibleOnGrid { get; private set; }

    /// <summary>Counted on the dashboards (records where the boolean is true); only for booleans.</summary>
    public bool DashboardCounter { get; private set; }

    public int Order { get; private set; }

    public bool HasOptions => Type is CustomFieldType.Select or CustomFieldType.MultiSelect;

    public static bool IsValidKey(string? key) => key is not null && KeyPattern().IsMatch(key);

    public static Result<CustomFieldDefinition> Create(Guid id, string entityType, string key, CustomFieldSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(entityType.Length, EntityTypeMaxLength);

        key = key?.Trim() ?? string.Empty;
        if (!IsValidKey(key))
        {
            return Errors.Configuration.CustomFieldInvalid("key", "validation.customFields.key");
        }

        if (!Enum.IsDefined(spec.Type))
        {
            return Errors.Configuration.CustomFieldInvalid("type", "validation.customFields.type");
        }

        var definition = new CustomFieldDefinition(id, entityType, key, spec.Type);
        var applied = definition.Update(spec);
        return applied.IsSuccess ? definition : Result.Failure<CustomFieldDefinition>(applied.Error!);
    }

    public Result Update(CustomFieldSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Type != Type)
        {
            return Errors.Configuration.CustomFieldInvalid("type", "validation.customFields.typeImmutable");
        }

        var label = spec.Label?.Trim() ?? string.Empty;
        if (label.Length is 0 or > LabelMaxLength)
        {
            return Errors.Configuration.CustomFieldInvalid("label", "validation.customFields.label");
        }

        var options = (spec.Options ?? []).Select(option => option?.Trim() ?? string.Empty).ToList();
        if (HasOptions
            ? options.Count is 0 or > MaxOptions
                || options.Any(option => option.Length is 0 or > OptionMaxLength)
                || options.Distinct(StringComparer.Ordinal).Count() != options.Count
            : options.Count > 0)
        {
            return Errors.Configuration.CustomFieldInvalid("options", "validation.customFields.options");
        }

        var group = string.IsNullOrWhiteSpace(spec.GroupName) ? null : spec.GroupName.Trim();
        if (group is { Length: > GroupNameMaxLength })
        {
            return Errors.Configuration.CustomFieldInvalid("groupName", "validation.customFields.groupName");
        }

        var color = string.IsNullOrWhiteSpace(spec.BadgeColor) ? null : spec.BadgeColor.Trim();
        if (color is not null && (group is null || !HexColor().IsMatch(color)))
        {
            return Errors.Configuration.CustomFieldInvalid("badgeColor", "validation.customFields.badgeColor");
        }

        if (spec.DashboardCounter && Type != CustomFieldType.Boolean)
        {
            return Errors.Configuration.CustomFieldInvalid("dashboardCounter", "validation.customFields.dashboardCounter");
        }

        if (spec.Order is < 0 or > MaxOrder)
        {
            return Errors.Configuration.CustomFieldInvalid("order", "validation.customFields.order");
        }

        Label = label;
        Options = options;
        IsRequired = spec.IsRequired;
        GroupName = group;
        BadgeColor = color;
        VisibleOnGrid = spec.VisibleOnGrid;
        DashboardCounter = spec.DashboardCounter;
        Order = spec.Order;
        return Result.Success();
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();
}
