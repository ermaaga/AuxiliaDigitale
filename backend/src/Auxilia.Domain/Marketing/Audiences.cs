using System.Globalization;

using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Marketing;

/// <summary>What a segment condition looks at (N01).</summary>
public enum SegmentField
{
    /// <summary>The business status of the client (<c>Active</c>, <c>Inactive</c>).</summary>
    Status,

    /// <summary>The employee in charge (user id), or none.</summary>
    Employee,

    Tag,

    /// <summary>A Client specialization the client holds.</summary>
    Specialization,

    /// <summary>A case of the service (not deleted).</summary>
    Service,

    /// <summary>A case in this status (not deleted).</summary>
    CaseStatus,

    /// <summary>Age in years from the birth date.</summary>
    Age,

    /// <summary>When the client was created.</summary>
    CreatedOn,

    /// <summary>A custom field of the client equal to a value (<see cref="SegmentCondition.Key"/>).</summary>
    CustomField,
}

public enum SegmentOperator
{
    Is,
    IsNot,
    None,
    Has,
    HasNot,
    AtLeast,
    AtMost,
    OnOrAfter,
    OnOrBefore,
}

/// <summary>One condition of a segment rule: the value is canonical text (a guid, a status, a number, a date <c>yyyy-MM-dd</c> or a JSON literal).</summary>
public sealed record SegmentCondition(SegmentField Field, SegmentOperator Operator, string? Value, string? Key = null);

/// <summary>Conditions joined by AND (<see cref="MatchAll"/>) or OR.</summary>
public sealed record SegmentGroup(bool MatchAll, IReadOnlyList<SegmentCondition> Conditions);

/// <summary>
/// The rule of a dynamic segment (N01): conditions and groups of conditions joined by AND or OR. Validated here, then
/// translated by the persistence layer into a parameterised query (no text from users reaches the SQL).
/// </summary>
public sealed record SegmentRule(bool MatchAll, IReadOnlyList<SegmentCondition> Conditions, IReadOnlyList<SegmentGroup> Groups)
{
    public const int MaxConditions = 50;
    public const int MaxGroups = 10;
    public const int MaxTextLength = 200;

    /// <summary>The operators each field accepts.</summary>
    public static IReadOnlyDictionary<SegmentField, SegmentOperator[]> Operators { get; } = new Dictionary<SegmentField, SegmentOperator[]>
    {
        [SegmentField.Status] = [SegmentOperator.Is, SegmentOperator.IsNot],
        [SegmentField.Employee] = [SegmentOperator.Is, SegmentOperator.IsNot, SegmentOperator.None],
        [SegmentField.Tag] = [SegmentOperator.Has, SegmentOperator.HasNot],
        [SegmentField.Specialization] = [SegmentOperator.Has, SegmentOperator.HasNot],
        [SegmentField.Service] = [SegmentOperator.Has, SegmentOperator.HasNot],
        [SegmentField.CaseStatus] = [SegmentOperator.Has, SegmentOperator.HasNot],
        [SegmentField.Age] = [SegmentOperator.AtLeast, SegmentOperator.AtMost],
        [SegmentField.CreatedOn] = [SegmentOperator.OnOrAfter, SegmentOperator.OnOrBefore],
        [SegmentField.CustomField] = [SegmentOperator.Is, SegmentOperator.IsNot],
    };

    private static readonly string[] ClientStatuses = ["Active", "Inactive"];
    private static readonly string[] CaseStatuses = ["Inserted", "InProgress", "Sent", "Completed"];

    public IEnumerable<SegmentCondition> AllConditions => Conditions.Concat(Groups.SelectMany(group => group.Conditions));

    /// <summary>Every error at once, keyed like the request (<c>rule.conditions[0].value</c>, <c>rule.groups[1].conditions[0].op</c>).</summary>
    public Result Validate()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (Groups.Count > MaxGroups || AllConditions.Count() > MaxConditions)
        {
            errors["rule"] = ["validation.segments.tooMany"];
        }

        if (!AllConditions.Any())
        {
            errors["rule"] = ["validation.segments.empty"];
        }

        Check(Conditions, "rule.conditions", errors);
        for (var index = 0; index < Groups.Count; index++)
        {
            if (Groups[index].Conditions.Count == 0)
            {
                errors[$"rule.groups[{index}]"] = ["validation.segments.emptyGroup"];
            }

            Check(Groups[index].Conditions, $"rule.groups[{index}].conditions", errors);
        }

        return errors.Count > 0 ? Errors.Marketing.SegmentInvalid(errors) : Result.Success();
    }

    private static void Check(IReadOnlyList<SegmentCondition> conditions, string prefix, Dictionary<string, string[]> errors)
    {
        for (var index = 0; index < conditions.Count; index++)
        {
            var condition = conditions[index];
            var path = $"{prefix}[{index}]";
            if (!Operators.TryGetValue(condition.Field, out var operators))
            {
                errors[$"{path}.field"] = ["validation.segments.field"];
                continue;
            }

            if (!operators.Contains(condition.Operator))
            {
                errors[$"{path}.op"] = ["validation.segments.op"];
                continue;
            }

            if (!ValueIsValid(condition))
            {
                errors[$"{path}.value"] = ["validation.segments.value"];
            }

            if (condition.Field == SegmentField.CustomField && string.IsNullOrWhiteSpace(condition.Key))
            {
                errors[$"{path}.key"] = ["validation.segments.key"];
            }
        }
    }

    private static bool ValueIsValid(SegmentCondition condition)
    {
        var value = condition.Value;
        if (value is { Length: > MaxTextLength })
        {
            return false;
        }

        return condition.Field switch
        {
            SegmentField.Status => ClientStatuses.Contains(value, StringComparer.Ordinal),
            SegmentField.Employee when condition.Operator == SegmentOperator.None => value is null,
            SegmentField.Employee or SegmentField.Tag or SegmentField.Specialization or SegmentField.Service => Guid.TryParse(value, out _),
            SegmentField.CaseStatus => CaseStatuses.Contains(value, StringComparer.Ordinal),
            SegmentField.Age => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var years) && years is >= 0 and <= 130,
            SegmentField.CreatedOn => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            SegmentField.CustomField => value is not null,
            _ => false,
        };
    }
}

/// <summary>A saved dynamic segment (<c>marketing.segments</c>, N01): a name and its rule (JSON).</summary>
public sealed class Segment : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    public Segment(Guid id)
        : base(id)
    {
        Name = Rule = string.Empty;
    }

    private Segment()
    {
        Name = Rule = string.Empty;
    }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    /// <summary>The <see cref="SegmentRule"/> as JSON.</summary>
    public string Rule { get; private set; }

    public Result Update(string? name, string? description, string rule)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaxLength)
        {
            errors["name"] = ["validation.segments.name"];
        }

        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (text is { Length: > DescriptionMaxLength })
        {
            errors["description"] = ["validation.segments.description"];
        }

        if (errors.Count > 0)
        {
            return Errors.Marketing.SegmentInvalid(errors);
        }

        Name = trimmed;
        Description = text;
        Rule = rule;
        return Result.Success();
    }
}

/// <summary>A static list of clients (<c>marketing.static_lists</c>, N01): chosen by hand, from a selection or imported.</summary>
public sealed class StaticList : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;

    public StaticList(Guid id)
        : base(id)
    {
        Name = string.Empty;
    }

    private StaticList()
    {
        Name = string.Empty;
    }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public Result Update(string? name, string? description)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaxLength)
        {
            return Errors.Marketing.ListInvalid("name", "validation.lists.name");
        }

        var text = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (text is { Length: > DescriptionMaxLength })
        {
            return Errors.Marketing.ListInvalid("description", "validation.lists.description");
        }

        Name = trimmed;
        Description = text;
        return Result.Success();
    }
}

/// <summary>A client in a static list (<c>marketing.static_list_members</c>): who added it and when.</summary>
public sealed class StaticListMember
{
    public StaticListMember(Guid listId, Guid personId, Guid? userId, DateTimeOffset now)
    {
        ListId = listId;
        PersonId = personId;
        AddedByUserId = userId;
        AddedAt = now;
    }

    private StaticListMember()
    {
    }

    public Guid ListId { get; private set; }

    public Guid PersonId { get; private set; }

    public Guid? AddedByUserId { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }
}
