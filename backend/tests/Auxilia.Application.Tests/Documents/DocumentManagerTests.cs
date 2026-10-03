using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Documents;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Cases.Public;
using Auxilia.Application.Configuration.Public;
using Auxilia.Application.Directory.Public;
using Auxilia.Application.Documents;
using Auxilia.Application.Tests.Directory;
using Auxilia.Application.Tests.Identity;
using Auxilia.Application.Tests.Platform;
using Auxilia.Contracts.Documents;
using Auxilia.Contracts.Messages.V1.Documents;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Documents;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Documents;

public sealed class DocumentManagerTests : IAsyncDisposable
{
    private static readonly Guid Admin = Guid.CreateVersion7();
    private static readonly Guid Employee = Guid.CreateVersion7();
    private static readonly Guid Client = Guid.CreateVersion7();
    private static readonly Guid Service = Guid.CreateVersion7();
    private static readonly Guid CaseId = Guid.CreateVersion7();
    private static readonly Guid Folder = Guid.CreateVersion7();
    private static readonly Guid SubFolder = Guid.CreateVersion7();

    private readonly MemoryStorage storage = new();
    private readonly InMemoryDocuments data = new();
    private readonly IClientDirectory clients = Substitute.For<IClientDirectory>();
    private readonly ICaseDirectory cases = Substitute.For<ICaseDirectory>();
    private readonly ICustomFieldValidator customFields = Substitute.For<ICustomFieldValidator>();
    private readonly IMessageOutbox outbox = Substitute.For<IMessageOutbox>();
    private readonly IRealtimeNotifier notifier = Substitute.For<IRealtimeNotifier>();
    private readonly ICurrentUser caller = Substitute.For<ICurrentUser>();
    private readonly ManualTimeProvider clock = new();
    private readonly DocumentManager manager;
    private readonly DocumentQueryService query;
    private CaseSummary caseSummary = new(CaseId, "2026-00001", Client, Service, "ISEE", true, true);

    public DocumentManagerTests()
    {
        CallAs(Admin, TenantRole.Administrator);
        var tenant = Substitute.For<ITenantContext>();
        tenant.Tenant.Returns(new TenantInfo(Guid.CreateVersion7(), "demo", TenantStatus.Active, "it", "Europe/Rome"));
        var files = new FileStore([storage], new FakeSettings(), tenant, clock, NullLogger<FileStore>.Instance);
        clients.FindAsync(Client, Arg.Any<CancellationToken>()).Returns(new ClientSummary(Client, "Mario Rossi", Employee));
        cases.FindAsync(CaseId, Arg.Any<CancellationToken>()).Returns(_ => caseSummary);
        cases.FoldersAsync(Service, Arg.Any<CancellationToken>()).Returns(
        [
            new FolderSummary(Folder, null, "Redditi", "Redditi"),
            new FolderSummary(SubFolder, Folder, "2025", "Redditi / 2025"),
        ]);
        customFields.ValidateAsync("document", Arg.Any<JsonElement?>(), Arg.Any<CancellationToken>()).Returns(Result.Success("{}"));
        var access = new DocumentAccess(caller, data, cases);
        manager = new DocumentManager(
            ManagerHarness.Runner(), data, files, clients, cases, customFields, access, outbox, notifier, caller, clock, NullLogger<DocumentManager>.Instance);
        query = new DocumentQueryService(data, files, cases, access);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Pdf(string text = "") => [.. "%PDF-1.7 "u8, .. System.Text.Encoding.UTF8.GetBytes(text)];

    public async ValueTask DisposeAsync() => await data.DisposeAsync();

    private void CallAs(Guid userId, params TenantRole[] roles)
    {
        caller.ActorType.Returns(ActorType.User);
        caller.UserId.Returns(userId);
        caller.Roles.Returns(roles);
    }

    private static UploadDocuments Request(Guid? caseId = null, Guid? folderId = null, string? fileName = null, params (string Name, byte[] Content)[] files) =>
        new(Client, caseId, folderId, null, null, null, fileName, null,
            (files.Length == 0 ? [("scan.pdf", Pdf())] : files).Select(file => new UploadedFile(new MemoryStream(file.Content), file.Name)).ToArray());

    private async Task<Guid> UploadAsync(UploadDocuments? request = null) => (await manager.UploadAsync(request ?? Request(), Ct)).Value.DocumentIds.Single();

    [Fact]
    public async Task Upload_CommitsTheFiles_StoresTheRows_AndQueuesTheCheck()
    {
        var uploaded = (await manager.UploadAsync(Request(CaseId, Folder, null, ("a b.pdf", Pdf("a")), ("c.pdf", Pdf("c"))), Ct)).Value;

        uploaded.DocumentIds.Count.ShouldBe(2);
        var first = data.Documents[0];
        (first.FileName, first.CaseId, first.FolderId, first.ReferenceYear, first.Status, first.UploadedByUserId)
            .ShouldBe(("a_b.pdf", (Guid?)CaseId, (Guid?)Folder, 2026, DocumentStatus.Processing, (Guid?)Admin));
        first.StorageKey.ShouldStartWith("tenants/demo/documents/2026/09/");
        storage.Files.Keys.ShouldBe(data.Documents.Select(document => document.StorageKey), ignoreOrder: true);
        await outbox.Received(2).EnqueueAsync(Arg.Any<IOperationScope>(), Arg.Any<ProcessDocumentCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_CustomName_GetsTheOriginalExtension_AndOnlyForOneFile()
    {
        await UploadAsync(Request(fileName: "Dichiarazione 2025"));
        data.Documents.Single().FileName.ShouldBe("Dichiarazione_2025.pdf");

        var many = await manager.UploadAsync(Request(null, null, "x", ("a.pdf", Pdf()), ("b.pdf", Pdf())), Ct);
        many.Error!.ValidationErrors.Keys.ShouldBe(["fileName"]);
    }

    [Fact]
    public async Task Upload_DuplicateNames_InTheBatchOrAlreadyThere_AreConflicts()
    {
        await UploadAsync();

        (await manager.UploadAsync(Request(), Ct)).Error!.Code.ShouldBe(EventCodes.Documents.DocumentNameTaken);
        (await manager.UploadAsync(Request(null, null, null, ("x.pdf", Pdf()), ("X.PDF", Pdf())), Ct)).Error!.Code.ShouldBe(EventCodes.Documents.DocumentNameTaken);

        // The same name in another folder of the case is fine.
        (await manager.UploadAsync(Request(CaseId, Folder), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Upload_InvalidMetadata_AreFieldErrors_AndNothingIsStored()
    {
        var result = await manager.UploadAsync(
            new UploadDocuments(Guid.CreateVersion7(), Guid.CreateVersion7(), Folder, 2000, Guid.CreateVersion7(), null, null, null, []), Ct);

        result.Error!.Code.ShouldBe(EventCodes.Documents.DocumentInvalid);
        result.Error.ValidationErrors.Keys.ShouldBe(["files", "referenceYear", "clientId", "caseId", "folderId", "areaId"], ignoreOrder: true);
        storage.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Upload_AFileRefused_RefusesTheBatch_AndRemovesWhatWasStaged()
    {
        var result = await manager.UploadAsync(Request(null, null, null, ("ok.pdf", Pdf()), ("fake.pdf", "MZ"u8.ToArray())), Ct);

        result.Error!.ValidationErrors.Keys.ShouldBe(["files[1]"]);
        storage.Files.ShouldBeEmpty();
        data.Documents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Employee_FollowsF10_ForCaseAndClientDocuments()
    {
        CallAs(Employee, TenantRole.Employee);
        data.Access = new DocumentClientAccess(AssignedToEmployee: false, HasPrivateSpecializationOutsideEmployee: false);

        // Case visible but not manageable: no upload (403), the document can be read but not changed.
        caseSummary = caseSummary with { CanManage = false };
        (await manager.UploadAsync(Request(CaseId), Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
        CallAs(Admin, TenantRole.Administrator);
        var caseDocument = await UploadAsync(Request(CaseId));
        CallAs(Employee, TenantRole.Employee);
        var detail = (await query.GetAsync(caseDocument, Ct)).Value;
        (detail.CanEdit, detail.CanManage).ShouldBe((false, false));
        (await manager.DeleteAsync(caseDocument, Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);

        // Case not visible: 404 everywhere.
        caseSummary = caseSummary with { CanSee = false };
        (await query.GetAsync(caseDocument, Ct)).Error!.Code.ShouldBe(EventCodes.Documents.DocumentNotFound);
        (await query.OpenAsync(caseDocument, Ct)).Error!.Code.ShouldBe(EventCodes.Documents.DocumentNotFound);

        // Without a case: a client with a private specialization the employee lacks is hidden, unless assigned.
        var loose = await UploadAsync();
        (await query.GetAsync(loose, Ct)).Value.CanEdit.ShouldBeFalse();
        data.Access = new DocumentClientAccess(false, true);
        (await query.GetAsync(loose, Ct)).IsFailure.ShouldBeTrue();
        data.Access = new DocumentClientAccess(true, true);
        (await query.GetAsync(loose, Ct)).Value.CanEdit.ShouldBeTrue();

        (await query.ListAsync(new DocumentListQuery(null, null, null, null, null, null, null, null, null, null, 1, 25), Ct)).IsSuccess.ShouldBeTrue();
        data.LastFilter!.Scope.ShouldBe(new DocumentScope(false, Employee));
    }

    [Fact]
    public async Task Update_KeepsTheExtension_ChecksAreaAndNames()
    {
        var id = await UploadAsync();
        await UploadAsync(Request(fileName: "other"));
        var area = DocumentArea.Create(Guid.CreateVersion7(), "Fiscale").Value;
        data.Areas.Add(area);

        (await manager.UpdateAsync(id, new UpdateDocumentRequest("Nuovo nome.docx", 2025, area.Id, "desc", null), Ct)).IsSuccess.ShouldBeTrue();
        var document = data.Documents.Single(item => item.Id == id);
        (document.FileName, document.AreaId, document.Description).ShouldBe(("Nuovo_nome.pdf", (Guid?)area.Id, "desc"));

        (await manager.UpdateAsync(id, new UpdateDocumentRequest("other", 2025, null, null, null), Ct)).Error!.Code.ShouldBe(EventCodes.Documents.DocumentNameTaken);
        (await manager.UpdateAsync(id, new UpdateDocumentRequest("x", 2025, Guid.CreateVersion7(), null, null), Ct)).Error!.ValidationErrors.Keys.ShouldBe(["areaId"]);
        (await manager.UpdateAsync(Guid.CreateVersion7(), new UpdateDocumentRequest("x", 2025, null, null, null), Ct)).Error!.Code
            .ShouldBe(EventCodes.Documents.DocumentNotFound);
    }

    [Fact]
    public async Task Move_OnlyToFoldersOfTheCasesService()
    {
        var id = await UploadAsync(Request(CaseId));
        var loose = await UploadAsync(Request(fileName: "loose"));

        (await manager.MoveAsync(id, SubFolder, Ct)).IsSuccess.ShouldBeTrue();
        data.Documents.Single(document => document.Id == id).FolderId.ShouldBe(SubFolder);
        (await manager.MoveAsync(id, Guid.CreateVersion7(), Ct)).Error!.ValidationErrors.Keys.ShouldBe(["folderId"]);
        (await manager.MoveAsync(loose, Folder, Ct)).Error!.ValidationErrors.Keys.ShouldBe(["folderId"]);
        (await manager.MoveAsync(id, null, Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_RemovesRowAndFile_Process_ChecksTheChecksum()
    {
        var kept = await UploadAsync(Request(fileName: "kept"));
        var deleted = await UploadAsync();
        var key = data.Documents.Single(document => document.Id == deleted).StorageKey;

        (await manager.DeleteAsync(deleted, Ct)).IsSuccess.ShouldBeTrue();
        storage.Files.ShouldNotContainKey(key);

        (await manager.ProcessAsync(kept, Ct)).IsSuccess.ShouldBeTrue();
        data.Documents.Single().Status.ShouldBe(DocumentStatus.Available);
        await notifier.Received(1).ToUserAsync(Admin, RealtimeEvents.DocumentProcessed, Arg.Is<DocumentProcessedEvent>(processed => processed.DocumentId == kept), Arg.Any<CancellationToken>());

        var damaged = await UploadAsync(Request(fileName: "damaged"));
        storage.Files[data.Documents.Single(document => document.Id == damaged).StorageKey] = [1, 2, 3];
        (await manager.ProcessAsync(damaged, Ct)).IsSuccess.ShouldBeTrue();
        data.Documents.Single(document => document.Id == damaged).Status.ShouldBe(DocumentStatus.Damaged);
        (await manager.ProcessAsync(Guid.CreateVersion7(), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Download_ReturnsTheFile_OrFileMissing()
    {
        var id = await UploadAsync();
        var content = (await query.OpenAsync(id, Ct)).Value;
        (content.FileName, content.ContentType).ShouldBe(("scan.pdf", "application/pdf"));
        await content.Content.DisposeAsync();

        storage.Files.Clear();
        (await query.OpenAsync(id, Ct)).Error!.Code.ShouldBe(EventCodes.Documents.DocumentFileMissing);
    }

    [Fact]
    public async Task Zip_KeepsTheFolderPaths_AndSuffixesDuplicates()
    {
        await UploadAsync(Request(CaseId, null, null, ("root.pdf", Pdf("r"))));
        await UploadAsync(Request(CaseId, Folder, null, ("a.pdf", Pdf("a"))));
        await UploadAsync(Request(CaseId, SubFolder, null, ("b.pdf", Pdf("b"))));

        (await EntriesAsync(null)).ShouldBe(["root.pdf", "Redditi/a.pdf", "Redditi/2025/b.pdf"], ignoreOrder: true);
        (await EntriesAsync(Folder)).ShouldBe(["a.pdf", "2025/b.pdf"], ignoreOrder: true);
        (await query.ZipAsync(Guid.CreateVersion7(), null, Ct)).Error!.Code.ShouldBe(EventCodes.Cases.CaseNotFound);
        (await query.ZipAsync(CaseId, Guid.CreateVersion7(), Ct)).Error!.ValidationErrors.Keys.ShouldBe(["folderId"]);
    }

    [Fact]
    public void ZipPaths_DuplicatesGetASuffix()
    {
        var folders = new[] { new FolderSummary(Folder, null, "Redditi", "Redditi") };
        Document Of(string name) => Document.Upload(
            Guid.CreateVersion7(),
            new DocumentUpload(Client, CaseId, Folder, name, "k" + Guid.NewGuid(), "application/pdf", 1, "h", 2026, null, null, "{}"),
            null,
            clock.GetUtcNow()).Value;

        var paths = ZipPaths.Plan([Of("x.pdf"), Of("X.pdf"), Of("x.pdf")], folders, null).Select(entry => entry.Path.ToLowerInvariant()).ToArray();

        paths.ShouldBe(["redditi/x.pdf", "redditi/x_1.pdf", "redditi/x_2.pdf"]);
    }

    [Fact]
    public async Task Areas_UniqueAmongActive()
    {
        var id = (await manager.CreateAreaAsync(new CreateDocumentAreaRequest("Fiscale"), Ct)).Value;

        (await manager.CreateAreaAsync(new CreateDocumentAreaRequest("fiscale"), Ct)).Error!.Code.ShouldBe(EventCodes.Documents.DocumentAreaNameTaken);
        (await manager.UpdateAreaAsync(id, new UpdateDocumentAreaRequest("Fisco", false), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.CreateAreaAsync(new CreateDocumentAreaRequest("Fisco"), Ct)).IsSuccess.ShouldBeTrue();
        (await manager.UpdateAreaAsync(id, new UpdateDocumentAreaRequest("Fisco", true), Ct)).Error!.Code.ShouldBe(EventCodes.Documents.DocumentAreaNameTaken);
        (await manager.UpdateAreaAsync(Guid.CreateVersion7(), new UpdateDocumentAreaRequest("x", true), Ct)).Error!.Code.ShouldBe(EventCodes.Documents.DocumentAreaNotFound);
        (await query.AreasAsync(Ct)).Count.ShouldBe(2);
    }

    private async Task<string[]> EntriesAsync(Guid? folderId)
    {
        var zip = (await query.ZipAsync(CaseId, folderId, Ct)).Value;
        await using (zip.Content)
        {
            using var archive = new ZipArchive(zip.Content, ZipArchiveMode.Read);
            return archive.Entries.Select(entry => entry.FullName).ToArray();
        }
    }
}

/// <summary>Documents in memory: the page applies no filter (the SQL is tested on PostgreSQL).</summary>
internal sealed class InMemoryDocuments : IDocumentDataFactory, IDocumentData
{
    public List<Document> Documents { get; } = [];

    public List<DocumentArea> Areas { get; } = [];

    public DocumentClientAccess Access { get; set; } = new(true, false);

    public DocumentFilter? LastFilter { get; private set; }

    public Task<IDocumentData> OpenAsync(CancellationToken cancellationToken) => Task.FromResult<IDocumentData>(this);

    public Task<(IReadOnlyList<DocumentRow> Items, int Total)> PageAsync(DocumentFilter filter, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        return Task.FromResult<(IReadOnlyList<DocumentRow>, int)>((Documents.Select(Row).ToArray(), Documents.Count));
    }

    public Task<DocumentRow?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Documents.SingleOrDefault(document => document.Id == id) is { } document ? Row(document) : null);

    public Task<Document?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Documents.SingleOrDefault(document => document.Id == id));

    public Task<IReadOnlyList<Document>> OfCaseAsync(Guid caseId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Document>>(Documents.Where(document => document.CaseId == caseId).ToArray());

    public Task<DocumentClientAccess> ClientAccessAsync(Guid clientId, Guid employeeUserId, CancellationToken cancellationToken) => Task.FromResult(Access);

    public Task<IReadOnlySet<string>> NamesTakenAsync(
        Guid clientId, Guid? caseId, Guid? folderId, IReadOnlyCollection<string> fileNames, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<string>>(Documents
            .Where(document => document.ClientId == clientId && document.CaseId == caseId && document.FolderId == folderId && document.Id != exceptId
                && fileNames.Contains(document.FileName, StringComparer.OrdinalIgnoreCase))
            .Select(document => document.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

    public Task<IReadOnlyList<DocumentAreaRow>> AreasAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DocumentAreaRow>>(Areas.Select(area => new DocumentAreaRow(area, 0)).ToArray());

    public Task<DocumentArea?> FindAreaAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Areas.SingleOrDefault(area => area.Id == id));

    public Task<bool> AreaNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken) =>
        Task.FromResult(Areas.Any(area => area.IsActive && string.Equals(area.Name, name, StringComparison.OrdinalIgnoreCase) && area.Id != exceptId));

    public void Add(Document document) => Documents.Add(document);

    public void Remove(Document document) => Documents.Remove(document);

    public void Add(DocumentArea area) => Areas.Add(area);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static DocumentRow Row(Document document) => new(document, "Mario Rossi", document.CaseId is null ? null : "2026-00001", "ISEE", null, "Admin");
}
