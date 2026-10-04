using System.Text.RegularExpressions;

using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Directory;

/// <summary>
/// A label staff put on clients (<c>directory.tags</c>, N01): a unique name (case-insensitive) and an optional badge
/// colour <c>#rrggbb</c>. Segments (M-02) select clients by tag.
/// </summary>
public sealed partial class Tag : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 50;

    private Tag(Guid id)
        : base(id)
    {
        Name = string.Empty;
    }

    private Tag()
    {
        Name = string.Empty;
    }

    public string Name { get; private set; }

    public string? Color { get; private set; }

    public static Result<Tag> Create(Guid id, string? name, string? color)
    {
        var tag = new Tag(id);
        var applied = tag.Update(name, color);
        return applied.IsFailure ? Result.Failure<Tag>(applied.Error!) : tag;
    }

    public Result Update(string? name, string? color)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaxLength)
        {
            errors["name"] = ["validation.tags.name"];
        }

        var badge = string.IsNullOrWhiteSpace(color) ? null : color.Trim().ToUpperInvariant();
        if (badge is not null && !ColorPattern().IsMatch(badge))
        {
            errors["color"] = ["validation.tags.color"];
        }

        if (errors.Count > 0)
        {
            return Errors.Directory.TagInvalid(errors);
        }

        Name = trimmed;
        Color = badge;
        return Result.Success();
    }

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}

/// <summary>A tag on a person (<c>directory.person_tags</c>): who put it and when.</summary>
public sealed class PersonTag
{
    public PersonTag(Guid personId, Guid tagId, Guid? userId, DateTimeOffset now)
    {
        PersonId = personId;
        TagId = tagId;
        AssignedByUserId = userId;
        AssignedAt = now;
    }

    private PersonTag()
    {
    }

    public Guid PersonId { get; private set; }

    public Guid TagId { get; private set; }

    public Guid? AssignedByUserId { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }
}

/// <summary>What a consent is for (N01).</summary>
public enum ConsentPurpose
{
    Marketing,
    Privacy,
}

/// <summary>How the person is contacted under the consent (WhatsApp later, N03).</summary>
public enum ConsentChannel
{
    Email,
    WhatsApp,
}

/// <summary>Where a consent change came from (N01, D-23).</summary>
public enum ConsentSource
{
    Staff,
    Import,
    Api,
    LegacyMigration,
}

/// <summary>
/// One change of a consent of a person (<c>directory.consents</c>, N01): granted or revoked for a purpose and a
/// channel, when, from where, the text version and who recorded it. Never changed: the latest change per purpose and
/// channel is the current consent.
/// </summary>
public sealed class Consent
{
    public const int VersionMaxLength = 50;
    public const int NoteMaxLength = 500;

    private Consent(Guid id, Guid personId, ConsentPurpose purpose, ConsentChannel channel, bool granted, ConsentSource source, DateTimeOffset now)
    {
        Id = id;
        PersonId = personId;
        Purpose = purpose;
        Channel = channel;
        Granted = granted;
        Source = source;
        RecordedAt = now;
    }

    private Consent()
    {
    }

    public Guid Id { get; private set; }

    public Guid PersonId { get; private set; }

    public ConsentPurpose Purpose { get; private set; }

    public ConsentChannel Channel { get; private set; }

    public bool Granted { get; private set; }

    public ConsentSource Source { get; private set; }

    /// <summary>The version of the consent text the person accepted, when known.</summary>
    public string? Version { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public Guid? RecordedByUserId { get; private set; }

    public static Result<Consent> Record(
        Guid id, Guid personId, ConsentPurpose purpose, ConsentChannel channel, bool granted, ConsentSource source, string? version, string? note,
        Guid? userId, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (!Enum.IsDefined(purpose))
        {
            errors["purpose"] = ["validation.consents.purpose"];
        }

        if (!Enum.IsDefined(channel))
        {
            errors["channel"] = ["validation.consents.channel"];
        }

        var cleanVersion = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        if (cleanVersion is { Length: > VersionMaxLength })
        {
            errors["version"] = ["validation.consents.version"];
        }

        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (cleanNote is { Length: > NoteMaxLength })
        {
            errors["note"] = ["validation.consents.note"];
        }

        if (errors.Count > 0)
        {
            return Errors.Directory.ConsentInvalid(errors);
        }

        return new Consent(id, personId, purpose, channel, granted, source, now) { Version = cleanVersion, Note = cleanNote, RecordedByUserId = userId };
    }
}
