using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Documents;

/// <summary>After the upload the Worker checks the stored file (F14 async processing).</summary>
public enum DocumentStatus
{
    Processing,
    Available,

    /// <summary>The stored file is missing or does not match the upload checksum.</summary>
    Damaged,
}

/// <summary>What an upload stores: the file already committed to the storage and its metadata.</summary>
public sealed record DocumentUpload(
    Guid ClientId,
    Guid? CaseId,
    Guid? FolderId,
    string FileName,
    string StorageKey,
    string ContentType,
    long Size,
    string Sha256,
    int ReferenceYear,
    Guid? AreaId,
    string? Description,
    string CustomFields);

/// <summary>
/// A file of a client (<c>documents.documents</c>, legacy <c>UserDocument</c>, F14): optionally of one of its cases and
/// filed in a folder of the case's service (F33). Reference year ≥ current − 10 (Q50); the name keeps its extension.
/// The file lives in the storage under <see cref="StorageKey"/>; deleting a document deletes the file.
/// </summary>
public sealed class Document : AggregateRoot<Guid>, IAuditable
{
    public const int FileNameMaxLength = 200;
    public const int DescriptionMaxLength = 1000;
    public const int StorageKeyMaxLength = 300;
    public const int ContentTypeMaxLength = 100;
    public const int YearsBack = 10;

    private Document(Guid id, DocumentUpload upload, Guid? uploadedByUserId, DateTimeOffset now)
        : base(id)
    {
        ClientId = upload.ClientId;
        CaseId = upload.CaseId;
        FolderId = upload.FolderId;
        FileName = upload.FileName;
        StorageKey = upload.StorageKey;
        ContentType = upload.ContentType;
        Size = upload.Size;
        Sha256 = upload.Sha256;
        ReferenceYear = upload.ReferenceYear;
        AreaId = upload.AreaId;
        Description = Text(upload.Description);
        CustomFields = upload.CustomFields;
        UploadedByUserId = uploadedByUserId;
        UploadedAt = now;
        Status = DocumentStatus.Processing;
    }

    private Document()
    {
        FileName = StorageKey = ContentType = Sha256 = CustomFields = string.Empty;
    }

    /// <summary>The client (its person id).</summary>
    public Guid ClientId { get; private set; }

    public Guid? CaseId { get; private set; }

    /// <summary>A folder of the template of the case's service (only with a case).</summary>
    public Guid? FolderId { get; private set; }

    public string FileName { get; private set; }

    public string StorageKey { get; private set; }

    public string ContentType { get; private set; }

    public long Size { get; private set; }

    /// <summary>SHA-256 of the content, hex lower case.</summary>
    public string Sha256 { get; private set; }

    public int ReferenceYear { get; private set; }

    public Guid? AreaId { get; private set; }

    public string? Description { get; private set; }

    public string CustomFields { get; private set; }

    public Guid? UploadedByUserId { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public DocumentStatus Status { get; private set; }

    public string Extension => Path.GetExtension(FileName);

    public static Result<Document> Upload(Guid id, DocumentUpload upload, Guid? uploadedByUserId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(upload);

        var errors = Check(upload.FileName, upload.ReferenceYear, upload.Description, DateOnly.FromDateTime(now.UtcDateTime));
        if (upload.FolderId is not null && upload.CaseId is null)
        {
            errors["folderId"] = ["validation.documents.folder"];
        }

        return errors.Count > 0 ? Errors.Documents.DocumentInvalid(errors) : new Document(id, upload, uploadedByUserId, now);
    }

    /// <summary>The errors of name, year and description (one per field).</summary>
    public static Dictionary<string, string[]> Check(string? fileName, int referenceYear, string? description, DateOnly today)
    {
        var errors = CheckMetadata(referenceYear, description, today);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > FileNameMaxLength || Path.GetExtension(fileName).Length < 2)
        {
            errors["fileName"] = ["validation.documents.fileName"];
        }

        return errors;
    }

    /// <summary>The errors of reference year (Q50: ≥ current − 10) and description.</summary>
    public static Dictionary<string, string[]> CheckMetadata(int referenceYear, string? description, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (referenceYear < today.Year - YearsBack || referenceYear > today.Year + 1)
        {
            errors["referenceYear"] = ["validation.documents.referenceYear"];
        }

        if (Text(description) is { Length: > DescriptionMaxLength })
        {
            errors["description"] = ["validation.documents.description"];
        }

        return errors;
    }

    /// <summary>Legacy detail: the name (its extension never changes), reference year, area, description, custom fields.</summary>
    public Result Update(string fileName, int referenceYear, Guid? areaId, string? description, string customFields, DateOnly today)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customFields);

        var errors = Check(fileName, referenceYear, description, today);
        if (!errors.ContainsKey("fileName") && !string.Equals(Path.GetExtension(fileName), Extension, StringComparison.OrdinalIgnoreCase))
        {
            errors["fileName"] = ["validation.documents.fileName"];
        }

        if (errors.Count > 0)
        {
            return Errors.Documents.DocumentInvalid(errors);
        }

        FileName = fileName;
        ReferenceYear = referenceYear;
        AreaId = areaId;
        Description = Text(description);
        CustomFields = customFields;
        return Result.Success();
    }

    /// <summary>To another folder of the case's service, or to none (F33).</summary>
    public Result MoveTo(Guid? folderId)
    {
        if (folderId is not null && CaseId is null)
        {
            return Errors.Documents.DocumentInvalid("folderId", "validation.documents.folder");
        }

        FolderId = folderId;
        return Result.Success();
    }

    /// <summary>The Worker found the stored file as uploaded (or not). Only once, from Processing.</summary>
    public bool Processed(bool intact)
    {
        if (Status != DocumentStatus.Processing)
        {
            return false;
        }

        Status = intact ? DocumentStatus.Available : DocumentStatus.Damaged;
        return true;
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// An area of the documents (<c>documents.document_areas</c>; legacy free text, now a managed list, F14). Inactive
/// areas stay on their documents and are not offered for new ones.
/// </summary>
public sealed class DocumentArea : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 100;

    private DocumentArea(Guid id, string name)
        : base(id)
    {
        Name = name;
        IsActive = true;
    }

    private DocumentArea()
    {
        Name = string.Empty;
    }

    /// <summary>Unique among the active areas (case-insensitive).</summary>
    public string Name { get; private set; }

    public bool IsActive { get; private set; }

    public static Result<DocumentArea> Create(Guid id, string? name) =>
        CheckName(name) is { } valid ? new DocumentArea(id, valid) : Errors.Documents.DocumentAreaInvalid();

    public Result Update(string? name, bool isActive)
    {
        if (CheckName(name) is not { } valid)
        {
            return Errors.Documents.DocumentAreaInvalid();
        }

        Name = valid;
        IsActive = isActive;
        return Result.Success();
    }

    private static string? CheckName(string? name) => name?.Trim() is { Length: > 0 and <= NameMaxLength } trimmed ? trimmed : null;
}
