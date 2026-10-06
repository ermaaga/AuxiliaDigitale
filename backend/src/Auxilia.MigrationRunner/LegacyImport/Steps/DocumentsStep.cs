using Auxilia.Application.Documents.Public;
using Auxilia.Domain.Cases;
using Auxilia.Domain.Directory;
using Auxilia.Domain.Documents;
using Auxilia.Domain.Identity;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-04 (F14, F33): <c>UserDocuments</c> → <c>documents.documents</c>, the distinct <c>Area</c> texts →
/// <c>documents.document_areas</c>, every file copied through the tenant's <see cref="IFileStore"/> (same checks as an
/// upload: type, content, size, SHA-256; key <c>tenants/{slug}/documents/…</c>). A document already imported keeps its
/// copy. With <c>--dry-run</c> files are staged to check them and deleted. Rules: mapping.md §5.3.
/// </summary>
internal sealed class DocumentsStep : ILegacyImportStep
{
    public const string Table = "UserDocuments";

    /// <summary>The storage area of documents (same as the Documents module).</summary>
    public const string StorageArea = "documents";

    private readonly IFileStore files;
    private readonly LegacyFiles legacyFiles;

    public DocumentsStep(IFileStore files, LegacyFiles legacyFiles)
    {
        this.files = files;
        this.legacyFiles = legacyFiles;
    }

    public string Name => "documents";

    /// <summary>An area name as staff typed it, with spaces collapsed; <c>null</c> when empty.</summary>
    public static string? AreaName(string? area)
    {
        var name = string.Join(' ', (area ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return name.Length == 0 ? null : name.Length > DocumentArea.NameMaxLength ? name[..DocumentArea.NameMaxLength] : name;
    }

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var db = context.Tenant;
        var rows = await context.Legacy.UserDocuments.OrderBy(row => row.Id).ToListAsync(cancellationToken);
        var result = context.Report.For(Table);
        var pending = rows.Where(row => context.Ids.Find(Table, row.Id) is null).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        if (!legacyFiles.IsAvailable)
        {
            foreach (var row in pending)
            {
                context.Report.Skip(Table, row.Id, "no files directory given (--files)");
            }

            return;
        }

        var areas = await AreasAsync(context, pending, cancellationToken);
        var clients = await (from user in db.Set<User>().AsNoTracking()
                             join profile in db.Set<ClientProfile>().AsNoTracking() on user.PersonId equals profile.Id
                             select new { user.Id, user.PersonId }).ToDictionaryAsync(row => row.Id, row => row.PersonId, cancellationToken);
        var cases = await db.Set<Case>().AsNoTracking()
            .Select(@case => new { @case.Id, @case.ClientId, @case.ServiceId }).ToDictionaryAsync(@case => @case.Id, cancellationToken);
        var folders = await db.Set<ServiceFolder>().AsNoTracking()
            .Select(folder => new { folder.Id, folder.ServiceId }).ToDictionaryAsync(folder => folder.Id, folder => folder.ServiceId, cancellationToken);

        foreach (var row in pending)
        {
            if (context.Ids.Find(UsersStep.Table, row.UserId) is not { } userId || !clients.TryGetValue(userId, out var clientId))
            {
                context.Report.Skip(Table, row.Id, "client not migrated or not a client");
                continue;
            }

            Guid? caseId = null;
            Guid? folderId = null;
            if (row.SubscriptionId is { } legacyCase)
            {
                if (context.Ids.Find(CasesStep.Table, legacyCase) is { } mapped && cases.TryGetValue(mapped, out var found) && found.ClientId == clientId)
                {
                    caseId = mapped;
                    if (row.FolderTemplateId is { } legacyFolder)
                    {
                        folderId = context.Ids.Find(ServiceCatalogStep.FoldersTable, legacyFolder) is { } folder
                            && folders.TryGetValue(folder, out var service) && service == found.ServiceId ? folder : null;
                        if (folderId is null)
                        {
                            context.Report.Warn(Table, row.Id, "folder not migrated or of another service: document in the case root");
                        }
                    }
                }
                else
                {
                    context.Report.Warn(Table, row.Id, "case not migrated or of another client: document of the client only");
                }
            }

            if (legacyFiles.Locate(row.FilePath) is not { } path)
            {
                context.Report.Skip(Table, row.Id, row.FilePath == "queued" ? "file never stored by the legacy upload queue" : "file not found");
                continue;
            }

            var copied = await CopyAsync(context, row, path, cancellationToken);
            if (copied is null)
            {
                continue;
            }

            var (key, staged) = copied.Value;
            var year = row.ReferenceYear;
            if (year < Case.MinDate.Year)
            {
                // The column was added with default 0: older documents take the year of their upload.
                year = TimeZoneInfo.ConvertTime(LegacyImportContext.Instant(row.UploadedAt), context.Zone).Year;
                context.Report.Warn(Table, row.Id, "reference year missing: year of the upload");
            }

            var fileName = LegacyFiles.OriginalName(row.FileName);
            var upload = new DocumentUpload(
                clientId, caseId, folderId, fileName.Length > Document.FileNameMaxLength ? staged.FileName : fileName, key, staged.ContentType, staged.Size, staged.Sha256,
                year, AreaName(row.Area) is { } area ? areas[area] : null, row.Description, CasesStep.CustomFields(row.CustomFields) ?? "{}");
            var uploadedBy = context.Ids.Find(UsersStep.Table, row.UploadedByUserId);
            var imported = Document.ImportLegacy(context.Ids.NewId(), upload, uploadedBy, LegacyImportContext.Instant(row.UploadedAt));
            if (imported.IsFailure)
            {
                context.Report.Skip(Table, row.Id, $"the document is not valid ({string.Join(", ", imported.Error!.ValidationErrors?.Keys ?? [])})");
                if (!context.DryRun)
                {
                    await files.DeleteAsync(key, cancellationToken);
                }

                continue;
            }

            db.Add(imported.Value);
            context.Ids.Add(Table, row.Id, imported.Value.Id);
            result.Created++;
        }
    }

    /// <summary>Stages the legacy file (checked like an upload) and commits it; in a dry run the staged copy is deleted.</summary>
    private async Task<(string Key, StagedFile Staged)?> CopyAsync(LegacyImportContext context, LegacyUserDocument row, string path, CancellationToken cancellationToken)
    {
        StagedFile staged;
        await using (var content = File.OpenRead(path))
        {
            var stagedResult = await files.StageAsync(content, LegacyFiles.OriginalName(row.FileName), cancellationToken);
            if (stagedResult.IsFailure)
            {
                context.Report.Skip(Table, row.Id, $"file refused by the document rules ({stagedResult.Error!.DisplayCode})");
                return null;
            }

            staged = stagedResult.Value;
        }

        if (row.FileSize > 0 && staged.Size != row.FileSize)
        {
            context.Report.Warn(Table, row.Id, "file size differs from the legacy record");
        }

        if (context.DryRun)
        {
            await files.DeleteAsync(staged.StagingKey, cancellationToken);
            return (staged.StagingKey, staged);
        }

        var committed = await files.CommitAsync(staged, StorageArea, cancellationToken);
        if (committed.IsFailure)
        {
            context.Report.Skip(Table, row.Id, $"file not copied ({committed.Error!.DisplayCode})");
            return null;
        }

        return (committed.Value, staged);
    }

    /// <summary>One area per distinct legacy text (matching an existing active area by name, case-insensitive).</summary>
    private static async Task<Dictionary<string, Guid>> AreasAsync(LegacyImportContext context, IEnumerable<LegacyUserDocument> rows, CancellationToken cancellationToken)
    {
        var areas = (await context.Tenant.Set<DocumentArea>().Where(area => area.IsActive).ToListAsync(cancellationToken))
            .ToDictionary(area => area.Name, area => area.Id, StringComparer.OrdinalIgnoreCase);
        var created = 0;
        foreach (var name in rows.Select(row => AreaName(row.Area)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (areas.ContainsKey(name))
            {
                continue;
            }

            var area = DocumentArea.Create(context.Ids.NewId(), name).Value;
            context.Tenant.Add(area);
            areas[name] = area.Id;
            created++;
        }

        context.Report.For("document areas").Created += created;
        return areas;
    }
}
