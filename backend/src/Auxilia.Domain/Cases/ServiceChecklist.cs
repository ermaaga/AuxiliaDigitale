using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Cases;

/// <summary>An item of a service checklist as edited: the id of an existing item, or none for a new one.</summary>
public sealed record ChecklistItemDetails(Guid? Id, string? Name, Guid? FolderId, bool Required);

/// <summary>
/// A document the cases of a service need (<c>cases.service_checklist_items</c>, B-26, F09): a name, optionally the
/// service folder where it goes, required or not, in order. A case ticks the items it has (<see cref="CaseChecklistMark"/>).
/// </summary>
public sealed class ServiceChecklistItem : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 200;
    public const int MaxItems = 100;

    public ServiceChecklistItem(Guid id, Guid serviceId)
        : base(id)
    {
        ServiceId = serviceId;
        Name = string.Empty;
    }

    private ServiceChecklistItem()
    {
        Name = string.Empty;
    }

    public Guid ServiceId { get; private set; }

    public string Name { get; private set; }

    public Guid? FolderId { get; private set; }

    public bool IsRequired { get; private set; }

    public int Order { get; private set; }

    public void Apply(ChecklistItemDetails details, int order)
    {
        ArgumentNullException.ThrowIfNull(details);
        Name = details.Name!.Trim();
        FolderId = details.FolderId;
        IsRequired = details.Required;
        Order = order;
    }

    /// <summary>The errors of a whole checklist (<c>items[i].name</c>), names unique (case-insensitive).</summary>
    public static Result Check(IReadOnlyList<ChecklistItemDetails> items, IReadOnlySet<Guid> folders)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(folders);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (items.Count > MaxItems)
        {
            errors["items"] = ["validation.checklist.tooMany"];
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < items.Count; index++)
        {
            var name = items[index].Name?.Trim() ?? string.Empty;
            if (name.Length is 0 or > NameMaxLength)
            {
                errors[$"items[{index}].name"] = ["validation.checklist.name"];
            }
            else if (!names.Add(name))
            {
                errors[$"items[{index}].name"] = ["validation.checklist.duplicate"];
            }

            if (items[index].FolderId is { } folder && !folders.Contains(folder))
            {
                errors[$"items[{index}].folderId"] = ["validation.checklist.folder"];
            }
        }

        return errors.Count > 0 ? Errors.Cases.ServiceChecklistInvalid(errors) : Result.Success();
    }
}

/// <summary>An item of its service checklist that a case has (<c>cases.case_checklist_marks</c>): who ticked it and when.</summary>
public sealed class CaseChecklistMark
{
    public CaseChecklistMark(Guid caseId, Guid itemId, Guid? userId, DateTimeOffset now)
    {
        CaseId = caseId;
        ItemId = itemId;
        CheckedByUserId = userId;
        CheckedAt = now;
    }

    private CaseChecklistMark()
    {
    }

    public Guid CaseId { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid? CheckedByUserId { get; private set; }

    public DateTimeOffset CheckedAt { get; private set; }
}
