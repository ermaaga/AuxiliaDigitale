using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Documents;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Cases.Public;
using Auxilia.Application.Configuration.Public;
using Auxilia.Application.Directory.Public;
using Auxilia.Application.Documents.Public;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Documents;
using Auxilia.Contracts.Messages.V1.Documents;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Documents;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Documents;

/// <summary>A file of an upload request, read once by the storage.</summary>
public sealed record UploadedFile(Stream Content, string? FileName);

/// <summary>
/// An upload (F14): every file gets the same client, case, folder, reference year (default this year), area,
/// description and custom fields; <see cref="FileName"/> (a custom name) only with one file, its original extension
/// appended when missing (legacy).
/// </summary>
public sealed record UploadDocuments(
    Guid ClientId,
    Guid? CaseId,
    Guid? FolderId,
    int? ReferenceYear,
    Guid? AreaId,
    string? Description,
    string? FileName,
    JsonElement? CustomFields,
    IReadOnlyList<UploadedFile> Files);

/// <summary>The content of a document to download.</summary>
public sealed record DocumentContent(Stream Content, string FileName, string ContentType);

/// <summary>Documents of the clients (F14, F33), managed by staff with the F10 rules.</summary>
public interface IDocumentManager
{
    /// <summary>
    /// Stages every file (type, content, size, checksum), then in one operation commits them and stores the rows; the
    /// Worker checks each stored file afterwards (status Processing → Available or Damaged).
    /// </summary>
    Task<Result<UploadDocumentsResponse>> UploadAsync(UploadDocuments request, CancellationToken cancellationToken);

    Task<Result> UpdateAsync(Guid id, UpdateDocumentRequest request, CancellationToken cancellationToken);

    Task<Result> MoveAsync(Guid id, Guid? folderId, CancellationToken cancellationToken);

    /// <summary>Deletes the row; the file goes after the commit.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Worker: compares the stored file with the upload checksum and tells the uploader (idempotent).</summary>
    Task<Result> ProcessAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<Guid>> CreateAreaAsync(CreateDocumentAreaRequest request, CancellationToken cancellationToken);

    Task<Result> UpdateAreaAsync(Guid id, UpdateDocumentAreaRequest request, CancellationToken cancellationToken);
}

public interface IDocumentQueryService
{
    Task<Result<PagedResponse<DocumentListItemResponse>>> ListAsync(DocumentListQuery query, CancellationToken cancellationToken);

    Task<Result<DocumentResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The file of a visible document; <c>AUX-16021</c> when the storage has lost it.</summary>
    Task<Result<DocumentContent>> OpenAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// A ZIP (temporary file, deleted when the stream is closed) of the documents of a case, or of one folder subtree
    /// (F33, fix <c>abf49b0</c>): paths of the folders, relative to the folder chosen; duplicate names suffixed
    /// <c>_1</c>, <c>_2</c>…; files missing in the storage skipped. <c>AUX-16021</c> when there is no document.
    /// </summary>
    Task<Result<DocumentContent>> ZipAsync(Guid caseId, Guid? folderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DocumentAreaResponse>> AreasAsync(CancellationToken cancellationToken);
}

/// <summary>What the caller may do on the documents of one client (and case).</summary>
internal sealed record DocumentRights(bool See, bool Manage, bool Edit)
{
    public static readonly DocumentRights Nothing = new(false, false, false);
}

/// <summary>
/// F10 for documents (D-04, code semantics): Administrators everything. Employees: documents of a case as the case
/// (see / manage, D-04); documents without a case visible when the client is assigned to them or has no private
/// specialization they lack, manageable when visible; metadata editable only for clients assigned to them (legacy).
/// </summary>
internal sealed class DocumentAccess(ICurrentUser currentUser, IDocumentDataFactory data, ICaseDirectory cases)
{
    public DocumentScope Scope() =>
        Has(TenantRole.Administrator) ? new DocumentScope(true, null)
        : Has(TenantRole.Employee) && currentUser.UserId is { } userId ? new DocumentScope(false, userId)
        : DocumentScope.None;

    public async Task<DocumentRights> OfAsync(Guid clientId, Guid? caseId, CancellationToken cancellationToken)
    {
        if (Has(TenantRole.Administrator))
        {
            return new DocumentRights(true, true, true);
        }

        if (!Has(TenantRole.Employee) || currentUser.UserId is not { } employee)
        {
            return DocumentRights.Nothing;
        }

        DocumentClientAccess client;
        await using (var store = await data.OpenAsync(cancellationToken))
        {
            client = await store.ClientAccessAsync(clientId, employee, cancellationToken);
        }

        if (caseId is { } id)
        {
            return await cases.FindAsync(id, cancellationToken) is { CanSee: true } found
                ? new DocumentRights(true, found.CanManage, client.AssignedToEmployee)
                : DocumentRights.Nothing;
        }

        var see = client.AssignedToEmployee || !client.HasPrivateSpecializationOutsideEmployee;
        return new DocumentRights(see, see, see && client.AssignedToEmployee);
    }

    private bool Has(TenantRole role) => currentUser.ActorType == ActorType.User && currentUser.Roles.Contains(role);
}

internal sealed class DocumentManager(
    IOperationRunner operations,
    IDocumentDataFactory data,
    IFileStore files,
    IClientDirectory clients,
    ICaseDirectory cases,
    ICustomFieldValidator customFields,
    DocumentAccess access,
    IMessageOutbox outbox,
    IRealtimeNotifier notifier,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<DocumentManager> logger) : IDocumentManager
{
    public const string CustomFieldEntity = "document";
    public const string StorageArea = "documents";
    public const int MaxFilesPerUpload = 50;

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<Result<UploadDocumentsResponse>> UploadAsync(UploadDocuments request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prepared = await PrepareAsync(request, cancellationToken);
        if (prepared.IsFailure)
        {
            return Result.Failure<UploadDocumentsResponse>(prepared.Error!);
        }

        var (names, fields) = prepared.Value;
        var staged = new List<StagedFile>();
        for (var index = 0; index < request.Files.Count; index++)
        {
            var stage = await files.StageAsync(request.Files[index].Content, request.Files[index].FileName, cancellationToken);
            if (stage.IsFailure)
            {
                await DeleteQuietlyAsync(staged.Select(file => file.StagingKey));
                return Result.Failure<UploadDocumentsResponse>(ForFile(stage.Error!, index));
            }

            staged.Add(stage.Value);
        }

        var committed = new List<string>();
        var uploaded = await operations.RunAsync<UploadDocumentsResponse>(Operations.Documents.Upload, new { request.ClientId, request.CaseId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var ids = new List<Guid>();
            for (var index = 0; index < staged.Count; index++)
            {
                var key = await files.CommitAsync(staged[index], StorageArea, cancellationToken);
                if (key.IsFailure)
                {
                    return Result.Failure<UploadDocumentsResponse>(key.Error!);
                }

                committed.Add(key.Value);
                var document = Document.Upload(
                    Guid.CreateVersion7(),
                    new DocumentUpload(
                        request.ClientId, request.CaseId, request.FolderId, names[index], key.Value, staged[index].ContentType, staged[index].Size,
                        staged[index].Sha256, request.ReferenceYear ?? Today.Year, request.AreaId, request.Description, fields),
                    currentUser.UserId,
                    clock.GetUtcNow());
                if (document.IsFailure)
                {
                    return Result.Failure<UploadDocumentsResponse>(document.Error!);
                }

                store.Add(document.Value);
                ids.Add(document.Value.Id);
            }

            await store.SaveChangesAsync(cancellationToken);
            foreach (var id in ids)
            {
                await outbox.EnqueueAsync(scope, new ProcessDocumentCommand(id), cancellationToken);
            }

            return new UploadDocumentsResponse(ids);
        }, cancellationToken);

        if (uploaded.IsFailure)
        {
            // Rolled back: no row points at these files any more.
            await DeleteQuietlyAsync(committed.Concat(staged.Skip(committed.Count).Select(file => file.StagingKey)));
        }

        return uploaded;
    }

    public Task<Result> UpdateAsync(Guid id, UpdateDocumentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Documents.Update, new { DocumentId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var loaded = await LoadAsync(store, id, rights => rights.Edit, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result.Failure(loaded.Error!);
            }

            var document = loaded.Value;
            if (request.AreaId is { } areaId && areaId != document.AreaId && await store.FindAreaAsync(areaId, cancellationToken) is not { IsActive: true })
            {
                return Errors.Documents.DocumentInvalid("areaId", "validation.documents.area");
            }

            var fields = await customFields.ValidateAsync(CustomFieldEntity, request.CustomFields, cancellationToken);
            if (fields.IsFailure)
            {
                return Result.Failure(fields.Error!);
            }

            // Legacy detail: the name is edited without the extension, which never changes.
            var name = FileNames.Sanitize(Path.GetFileNameWithoutExtension(request.FileName ?? string.Empty) + document.Extension);
            if ((await store.NamesTakenAsync(document.ClientId, document.CaseId, document.FolderId, [name], id, cancellationToken)).Count > 0)
            {
                return Errors.Documents.DocumentNameTaken();
            }

            var updated = document.Update(name, request.ReferenceYear, request.AreaId, request.Description, fields.Value, Today);
            if (updated.IsFailure)
            {
                return updated;
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    public Task<Result> MoveAsync(Guid id, Guid? folderId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Documents.Move, new { DocumentId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var loaded = await LoadAsync(store, id, rights => rights.Manage, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result.Failure(loaded.Error!);
            }

            var document = loaded.Value;
            if (folderId is { } folder && (document.CaseId is not { } caseId || !await FolderOfCaseAsync(caseId, folder, cancellationToken)))
            {
                return Errors.Documents.DocumentInvalid("folderId", "validation.documents.folder");
            }

            if ((await store.NamesTakenAsync(document.ClientId, document.CaseId, folderId, [document.FileName], id, cancellationToken)).Count > 0)
            {
                return Errors.Documents.DocumentNameTaken();
            }

            var moved = document.MoveTo(folderId);
            if (moved.IsFailure)
            {
                return moved;
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Documents.Delete, new { DocumentId = id }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var loaded = await LoadAsync(store, id, rights => rights.Manage, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result.Failure(loaded.Error!);
            }

            var key = loaded.Value.StorageKey;
            store.Remove(loaded.Value);
            await store.SaveChangesAsync(cancellationToken);
            scope.OnCommitted(async ct =>
            {
                if (await files.DeleteAsync(key, ct) is { IsFailure: true } failed)
                {
                    throw new IOException($"The file of document {id} was not deleted ({failed.Error!.DisplayCode}).");
                }
            });
            return Result.Success();
        }, cancellationToken);

    public Task<Result> ProcessAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Documents.Process, new { DocumentId = id }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(id, cancellationToken) is not { Status: DocumentStatus.Processing } document)
            {
                // Deleted, or already processed (a redelivered message).
                return Result.Success();
            }

            var opened = await files.OpenReadAsync(document.StorageKey, cancellationToken);
            if (opened.IsFailure)
            {
                return Result.Failure(opened.Error!);
            }

            var intact = false;
            if (opened.Value is { } content)
            {
                await using (content)
                {
                    intact = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, cancellationToken)) == document.Sha256;
                }
            }

            if (!intact)
            {
                Log.Documents.DocumentIntegrityFailed(logger, id);
            }

            document.Processed(intact);
            await store.SaveChangesAsync(cancellationToken);
            if (document.UploadedByUserId is { } uploader)
            {
                var processed = new DocumentProcessedEvent(id, document.FileName, document.Status.ToString());
                scope.OnCommitted(ct => notifier.ToUserAsync(uploader, RealtimeEvents.DocumentProcessed, processed, ct));
            }

            return Result.Success();
        }, cancellationToken);

    public Task<Result<Guid>> CreateAreaAsync(CreateDocumentAreaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Documents.CreateArea, null, async scope =>
        {
            var created = DocumentArea.Create(Guid.CreateVersion7(), request.Name);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error!);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.AreaNameTakenAsync(created.Value.Name, null, cancellationToken))
            {
                return Errors.Documents.DocumentAreaNameTaken();
            }

            store.Add(created.Value);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(DocumentArea), created.Value.Id);
            return Result.Success(created.Value.Id);
        }, cancellationToken);
    }

    public Task<Result> UpdateAreaAsync(Guid id, UpdateDocumentAreaRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Documents.UpdateArea, new { DocumentAreaId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAreaAsync(id, cancellationToken) is not { } area)
            {
                return Errors.Documents.DocumentAreaNotFound();
            }

            if (request.IsActive && request.Name?.Trim() is { Length: > 0 } name && await store.AreaNameTakenAsync(name, id, cancellationToken))
            {
                return Errors.Documents.DocumentAreaNameTaken();
            }

            var updated = area.Update(request.Name, request.IsActive);
            if (updated.IsFailure)
            {
                return updated;
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }

    /// <summary>The checks before any file is staged: who, where, metadata and names (all at once where possible).</summary>
    private async Task<Result<(string[] Names, string CustomFields)>> PrepareAsync(UploadDocuments request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (request.Files.Count is 0 or > MaxFilesPerUpload)
        {
            errors["files"] = ["validation.documents.files"];
        }

        if (!string.IsNullOrWhiteSpace(request.FileName) && request.Files.Count > 1)
        {
            errors["fileName"] = ["validation.documents.customName"];
        }

        var year = request.ReferenceYear ?? Today.Year;
        foreach (var (field, messages) in Document.CheckMetadata(year, request.Description, Today))
        {
            errors[field] = messages;
        }

        if (await clients.FindAsync(request.ClientId, cancellationToken) is null)
        {
            errors["clientId"] = ["validation.documents.client"];
        }

        var @case = request.CaseId is { } caseId ? await cases.FindAsync(caseId, cancellationToken) : null;
        if (request.CaseId is not null && (@case is null || @case.ClientId != request.ClientId))
        {
            errors["caseId"] = ["validation.documents.case"];
        }

        if (request.FolderId is { } folderId && (@case is null || !(await cases.FoldersAsync(@case.ServiceId, cancellationToken)).Any(folder => folder.Id == folderId)))
        {
            errors["folderId"] = ["validation.documents.folder"];
        }

        await using var store = await data.OpenAsync(cancellationToken);
        if (request.AreaId is { } areaId && await store.FindAreaAsync(areaId, cancellationToken) is not { IsActive: true })
        {
            errors["areaId"] = ["validation.documents.area"];
        }

        if (errors.Count > 0)
        {
            return Errors.Documents.DocumentInvalid(errors);
        }

        var rights = await access.OfAsync(request.ClientId, request.CaseId, cancellationToken);
        if (!rights.Manage)
        {
            return rights.See ? Errors.Identity.PermissionDenied() : Errors.Documents.DocumentInvalid("clientId", "validation.documents.client");
        }

        var fields = await customFields.ValidateAsync(CustomFieldEntity, request.CustomFields, cancellationToken);
        if (fields.IsFailure)
        {
            return Result.Failure<(string[], string)>(fields.Error!);
        }

        var names = request.Files.Select(file => Name(request.FileName, file.FileName)).ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length
            || (await store.NamesTakenAsync(request.ClientId, request.CaseId, request.FolderId, names, null, cancellationToken)).Count > 0)
        {
            return Errors.Documents.DocumentNameTaken();
        }

        return (names, fields.Value);
    }

    /// <summary>Legacy: a custom name gets the original extension when it has not that one.</summary>
    private static string Name(string? customName, string? originalName)
    {
        var original = FileNames.Sanitize(originalName);
        if (string.IsNullOrWhiteSpace(customName))
        {
            return original;
        }

        var extension = Path.GetExtension(original);
        var custom = FileNames.Sanitize(customName);
        return string.Equals(Path.GetExtension(custom), extension, StringComparison.OrdinalIgnoreCase) ? custom : custom + extension;
    }

    private static Error ForFile(Error error, int index) =>
        error.Type == ErrorType.Validation
            ? Error.Validation(error.Code, error.Description, error.ValidationErrors.ToDictionary(entry => $"files[{index}]", entry => entry.Value, StringComparer.Ordinal))
            : error;

    private async Task<Result<Document>> LoadAsync(IDocumentData store, Guid id, Func<DocumentRights, bool> allowed, CancellationToken cancellationToken)
    {
        if (await store.FindAsync(id, cancellationToken) is not { } document)
        {
            return Errors.Documents.DocumentNotFound();
        }

        var rights = await access.OfAsync(document.ClientId, document.CaseId, cancellationToken);
        return !rights.See ? Errors.Documents.DocumentNotFound()
            : !allowed(rights) ? Errors.Identity.PermissionDenied()
            : document;
    }

    private async Task<bool> FolderOfCaseAsync(Guid caseId, Guid folderId, CancellationToken cancellationToken) =>
        await cases.FindAsync(caseId, cancellationToken) is { } found
        && (await cases.FoldersAsync(found.ServiceId, cancellationToken)).Any(folder => folder.Id == folderId);

    private async Task DeleteQuietlyAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys.ToArray())
        {
            // Failures are logged by the file store; staging leftovers are cleaned by the storage lifecycle rule.
            await files.DeleteAsync(key, CancellationToken.None);
        }
    }
}

internal sealed class DocumentQueryService(IDocumentDataFactory data, IFileStore files, ICaseDirectory cases, DocumentAccess access) : IDocumentQueryService
{
    public const int MaxPageSize = 100;
    public const int MaxFilterLength = 200;

    private static readonly Dictionary<string, DocumentSort> Sorts = new(StringComparer.Ordinal)
    {
        ["uploadedAt"] = DocumentSort.UploadedAt,
        ["client"] = DocumentSort.Client,
        ["fileName"] = DocumentSort.FileName,
        ["referenceYear"] = DocumentSort.ReferenceYear,
        ["area"] = DocumentSort.Area,
        ["uploadedBy"] = DocumentSort.UploadedBy,
    };

    public async Task<Result<PagedResponse<DocumentListItemResponse>>> ListAsync(DocumentListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.Page < 1)
        {
            errors["page"] = ["validation.paging.page"];
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["validation.paging.pageSize"];
        }

        var sortField = query.Sort?.TrimStart('-');
        var sort = DocumentSort.UploadedAt;
        if (!string.IsNullOrEmpty(sortField) && !Sorts.TryGetValue(sortField, out sort))
        {
            errors["sort"] = ["validation.paging.sort"];
        }

        string?[] texts = [query.ClientName, query.FileName, query.Description, query.UploadedBy];
        if (texts.Any(text => text is { Length: > MaxFilterLength }))
        {
            errors["search"] = ["validation.paging.search"];
        }

        if (errors.Count > 0)
        {
            return Errors.Host.ValidationFailed(errors);
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var (items, total) = await store.PageAsync(
            new DocumentFilter(
                access.Scope(),
                query.ClientId,
                query.CaseId,
                query.FolderId,
                Text(query.ClientName),
                Text(query.FileName),
                Text(query.Description),
                Text(query.UploadedBy),
                query.ReferenceYear,
                query.AreaId,
                sort,
                string.IsNullOrEmpty(query.Sort) || query.Sort.StartsWith('-'),
                (query.Page - 1) * query.PageSize,
                query.PageSize),
            cancellationToken);
        return new PagedResponse<DocumentListItemResponse>(items.Select(ToListItem).ToArray(), query.Page, query.PageSize, total);
    }

    public async Task<Result<DocumentResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var visible = await VisibleAsync(id, cancellationToken);
        if (visible.IsFailure)
        {
            return Result.Failure<DocumentResponse>(visible.Error!);
        }

        var (row, rights) = visible.Value;
        var document = row.Document;
        DocumentFolderResponse? folder = null;
        if (document.FolderId is { } folderId && document.CaseId is { } caseId && await cases.FindAsync(caseId, cancellationToken) is { } found)
        {
            folder = (await cases.FoldersAsync(found.ServiceId, cancellationToken))
                .Where(item => item.Id == folderId)
                .Select(item => new DocumentFolderResponse(item.Id, item.Path))
                .FirstOrDefault();
        }

        using var fields = JsonDocument.Parse(document.CustomFields);
        var item = ToListItem(row);
        return new DocumentResponse(
            item.Id, item.Client, item.Case, folder, item.FileName, item.ContentType, item.Size, document.Sha256, item.ReferenceYear, item.Area,
            item.Description, fields.RootElement.Clone(), item.UploadedBy, item.UploadedAt, item.Status, rights.Edit, rights.Manage);
    }

    public async Task<Result<DocumentContent>> OpenAsync(Guid id, CancellationToken cancellationToken)
    {
        var visible = await VisibleAsync(id, cancellationToken);
        if (visible.IsFailure)
        {
            return Result.Failure<DocumentContent>(visible.Error!);
        }

        var document = visible.Value.Row.Document;
        var opened = await files.OpenReadAsync(document.StorageKey, cancellationToken);
        return opened.IsFailure ? Result.Failure<DocumentContent>(opened.Error!)
            : opened.Value is { } content ? new DocumentContent(content, document.FileName, document.ContentType)
            : Errors.Documents.DocumentFileMissing();
    }

    public async Task<Result<DocumentContent>> ZipAsync(Guid caseId, Guid? folderId, CancellationToken cancellationToken)
    {
        if (await cases.FindAsync(caseId, cancellationToken) is not { CanSee: true } found
            || !(await access.OfAsync(found.ClientId, caseId, cancellationToken)).See)
        {
            return Errors.Cases.CaseNotFound();
        }

        var folders = await cases.FoldersAsync(found.ServiceId, cancellationToken);
        if (folderId is { } chosen && folders.All(folder => folder.Id != chosen))
        {
            return Errors.Documents.DocumentInvalid("folderId", "validation.documents.folder");
        }

        IReadOnlyList<Document> documents;
        await using (var store = await data.OpenAsync(cancellationToken))
        {
            documents = await store.OfCaseAsync(caseId, cancellationToken);
        }

        var entries = ZipPaths.Plan(documents, folders, folderId);
        if (entries.Count == 0)
        {
            return Errors.Documents.DocumentFileMissing();
        }

        var zip = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, document) in entries)
                {
                    var opened = await files.OpenReadAsync(document.StorageKey, cancellationToken);
                    if (opened.IsFailure)
                    {
                        await zip.DisposeAsync();
                        return Result.Failure<DocumentContent>(opened.Error!);
                    }

                    // Legacy: files missing in the storage are skipped.
                    if (opened.Value is not { } content)
                    {
                        continue;
                    }

                    await using (content)
                    {
                        await using var entry = archive.CreateEntry(path, CompressionLevel.Optimal).Open();
                        await content.CopyToAsync(entry, cancellationToken);
                    }
                }
            }

            zip.Position = 0;
            var name = folderId is { } root ? folders.Single(folder => folder.Id == root).Name : found.ServiceName;
            return new DocumentContent(zip, FileNames.Sanitize(name + ".zip") is { Length: > 4 } fileName ? fileName : "documents.zip", "application/zip");
        }
        catch
        {
            await zip.DisposeAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<DocumentAreaResponse>> AreasAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.AreasAsync(cancellationToken))
            .Select(row => new DocumentAreaResponse(row.Area.Id, row.Area.Name, row.Area.IsActive, row.DocumentCount))
            .ToArray();
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DocumentListItemResponse ToListItem(DocumentRow row)
    {
        var document = row.Document;
        return new DocumentListItemResponse(
            document.Id,
            new DocumentClientResponse(document.ClientId, row.ClientName),
            document.CaseId is { } caseId ? new DocumentCaseResponse(caseId, row.CaseNumber ?? string.Empty, row.CaseServiceName ?? string.Empty) : null,
            document.FolderId,
            document.FileName,
            document.ContentType,
            document.Size,
            document.ReferenceYear,
            document.AreaId is { } areaId ? new DocumentAreaRefResponse(areaId, row.AreaName ?? string.Empty) : null,
            document.Description,
            document.UploadedByUserId is { } uploader ? new DocumentUserResponse(uploader, row.UploadedByName ?? string.Empty) : null,
            document.UploadedAt,
            document.Status.ToString());
    }

    private async Task<Result<(DocumentRow Row, DocumentRights Rights)>> VisibleAsync(Guid id, CancellationToken cancellationToken)
    {
        DocumentRow? row;
        await using (var store = await data.OpenAsync(cancellationToken))
        {
            row = await store.GetAsync(id, cancellationToken);
        }

        if (row is null)
        {
            return Errors.Documents.DocumentNotFound();
        }

        var rights = await access.OfAsync(row.Document.ClientId, row.Document.CaseId, cancellationToken);
        return rights.See ? (row, rights) : Errors.Documents.DocumentNotFound();
    }
}

/// <summary>The paths inside a case ZIP (F33, branch <c>fix/zip-download-folder</c>, part of the D-30 baseline).</summary>
internal static class ZipPaths
{
    /// <summary>
    /// Whole case: every document under the path of its folder, documents without a folder at the root. One folder F:
    /// the documents of F at the root and those of its subfolders under their path relative to F. Duplicate paths get
    /// <c>_1</c>, <c>_2</c>… before the extension.
    /// </summary>
    public static IReadOnlyList<(string Path, Document Document)> Plan(IReadOnlyList<Document> documents, IReadOnlyList<FolderSummary> folders, Guid? rootId)
    {
        var byId = folders.ToDictionary(folder => folder.Id);
        IReadOnlyList<string>? Segments(Guid? folderId)
        {
            // From the folder up to the root chosen (exclusive) or the top; null when not under the root chosen.
            var segments = new List<string>();
            for (var current = folderId; current != rootId;)
            {
                if (current is null || !byId.TryGetValue(current.Value, out var folder))
                {
                    return null;
                }

                segments.Insert(0, folder.Name);
                current = folder.ParentId;
            }

            return segments;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plan = new List<(string, Document)>();
        foreach (var document in documents.OrderBy(document => document.UploadedAt).ThenBy(document => document.Id))
        {
            if ((rootId is null && document.FolderId is null ? [] : Segments(document.FolderId)) is not { } segments)
            {
                continue;
            }

            var path = string.Join('/', segments.Append(document.FileName));
            var unique = path;
            for (var copy = 1; !used.Add(unique); copy++)
            {
                var extension = Path.GetExtension(path);
                unique = $"{path[..^extension.Length]}_{copy}{extension}";
            }

            plan.Add((unique, document));
        }

        return plan;
    }
}
