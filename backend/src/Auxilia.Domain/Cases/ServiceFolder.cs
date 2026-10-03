using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Cases;

/// <summary>
/// A folder of the folder template of a service (<c>cases.service_folders</c>, legacy <c>MembershipFolderTemplate</c>,
/// F33): a tree under the service, ordered among siblings. The documents of a case are filed in these folders (B-12);
/// deleting a folder deletes its subfolders and leaves their documents without a folder.
/// </summary>
public sealed class ServiceFolder : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 100;

    /// <summary>Levels below the root (a folder at the root is level 1).</summary>
    public const int MaxDepth = 10;

    /// <summary>Folders in the template of one service.</summary>
    public const int MaxFoldersPerService = 500;

    private ServiceFolder(Guid id, Guid serviceId, Guid? parentId, string name, int sortOrder)
        : base(id)
    {
        ServiceId = serviceId;
        ParentId = parentId;
        Name = name;
        SortOrder = sortOrder;
    }

    private ServiceFolder()
    {
        Name = string.Empty;
    }

    public Guid ServiceId { get; private set; }

    /// <summary><c>null</c> for a folder at the root.</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>Unique among its siblings (case-insensitive, checked by the manager).</summary>
    public string Name { get; private set; }

    public int SortOrder { get; private set; }

    /// <summary>Legacy: a new folder goes after its siblings.</summary>
    public static Result<ServiceFolder> Create(Guid id, Guid serviceId, Guid? parentId, string? name, int sortOrder)
    {
        var checkedName = CheckName(name);
        return checkedName.IsFailure
            ? Result.Failure<ServiceFolder>(checkedName.Error!)
            : new ServiceFolder(id, serviceId, parentId, checkedName.Value, sortOrder);
    }

    /// <summary>Trimmed; no path separators (the ZIP keeps folder paths).</summary>
    public static Result<string> CheckName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > NameMaxLength || trimmed.IndexOfAny(['/', '\\']) >= 0 || trimmed is "." or ".."
            ? Errors.Cases.ServiceFolderInvalid("name", "validation.serviceFolders.name")
            : trimmed;
    }

    public Result Rename(string? name)
    {
        var checkedName = CheckName(name);
        if (checkedName.IsFailure)
        {
            return Result.Failure(checkedName.Error!);
        }

        Name = checkedName.Value;
        return Result.Success();
    }

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;
}
