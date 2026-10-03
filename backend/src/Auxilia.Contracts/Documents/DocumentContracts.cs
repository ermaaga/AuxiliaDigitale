using System.Text.Json;

namespace Auxilia.Contracts.Documents;

public sealed record DocumentClientResponse(Guid Id, string FullName);

public sealed record DocumentCaseResponse(Guid Id, string Number, string ServiceName);

/// <summary>The folder of a document with its path (<c>A / B / C</c>, F33).</summary>
public sealed record DocumentFolderResponse(Guid Id, string Path);

public sealed record DocumentAreaRefResponse(Guid Id, string Name);

public sealed record DocumentUserResponse(Guid UserId, string FullName);

/// <summary>
/// A row of the document lists (F14): <c>status</c> <c>Processing</c> (checked by the Worker after the upload),
/// <c>Available</c> or <c>Damaged</c>.
/// </summary>
public sealed record DocumentListItemResponse(
    Guid Id,
    DocumentClientResponse Client,
    DocumentCaseResponse? Case,
    Guid? FolderId,
    string FileName,
    string ContentType,
    long Size,
    int ReferenceYear,
    DocumentAreaRefResponse? Area,
    string? Description,
    DocumentUserResponse? UploadedBy,
    DateTimeOffset UploadedAt,
    string Status);

/// <summary>A document (F14) with its folder path and what the caller may do: edit metadata, move and delete (F10).</summary>
public sealed record DocumentResponse(
    Guid Id,
    DocumentClientResponse Client,
    DocumentCaseResponse? Case,
    DocumentFolderResponse? Folder,
    string FileName,
    string ContentType,
    long Size,
    string Sha256,
    int ReferenceYear,
    DocumentAreaRefResponse? Area,
    string? Description,
    JsonElement CustomFields,
    DocumentUserResponse? UploadedBy,
    DateTimeOffset UploadedAt,
    string Status,
    bool CanEdit,
    bool CanManage);

/// <summary>The documents just uploaded (in the order of the files).</summary>
public sealed record UploadDocumentsResponse(IReadOnlyList<Guid> DocumentIds);

/// <summary>The name keeps its extension (legacy detail: the extension is never changed); reference year ≥ current − 10.</summary>
public sealed record UpdateDocumentRequest(string FileName, int ReferenceYear, Guid? AreaId, string? Description, JsonElement? CustomFields);

/// <summary>A folder of the case's service, or null for none (F33).</summary>
public sealed record MoveDocumentRequest(Guid? FolderId);

/// <summary>
/// The document lists (F14): <c>clientId</c> (client 360°), <c>caseId</c> (case documents), <c>folderId</c> (one
/// folder), text filters <c>clientName</c>, <c>fileName</c>, <c>description</c>, <c>uploadedBy</c>, exact
/// <c>referenceYear</c> and <c>areaId</c>; <c>sort</c> one of <c>uploadedAt</c> (default, descending), <c>client</c>,
/// <c>fileName</c>, <c>referenceYear</c>, <c>area</c>, <c>uploadedBy</c>, <c>-</c> for descending.
/// </summary>
public sealed record DocumentListQuery(
    Guid? ClientId,
    Guid? CaseId,
    Guid? FolderId,
    string? ClientName,
    string? FileName,
    string? Description,
    string? UploadedBy,
    int? ReferenceYear,
    Guid? AreaId,
    string? Sort,
    int Page,
    int PageSize);

public sealed record DocumentAreaResponse(Guid Id, string Name, bool IsActive, int DocumentCount);

public sealed record CreateDocumentAreaRequest(string Name);

public sealed record UpdateDocumentAreaRequest(string Name, bool IsActive);
