using System.Security.Cryptography;

using Auxilia.Domain.Documents;
using Auxilia.MigrationRunner.LegacyImport;
using Auxilia.Persistence.Tenant.Operations;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.LegacyImport.Tests;

/// <summary>E-04 end to end: documents, areas and files copied through the tenant's file store.</summary>
[Collection(LegacyDatabaseGroup.Name)]
public sealed class DocumentsImportTests(LegacyDatabaseFixture fixture) : IDisposable
{
    private static readonly byte[] Pdf = "%PDF-1.4"u8.ToArray();
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0];

    private readonly string files = Directory.CreateTempSubdirectory("legacy-files-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(files, recursive: true);

    [Fact]
    public async Task Import_DocumentsAndFiles_FollowTheMapping_AndRepeats()
    {
        WriteFiles();
        await using var source = await LegacyAsync("legacy_documents");
        await using var tenant = await fixture.CreateTenantAsync("tenant_documents");
        var store = new MemoryFileStore();

        var report = await ImportHarness.ImportAsync(source, tenant, files: store, filesRoot: files);

        var result = report.Tables["UserDocuments"];
        (result.Created, result.Skipped).ShouldBe((2, 4));
        report.Tables["document areas"].Created.ShouldBe(1);
        Reason(report, 3).ShouldBe("file not found");
        Reason(report, 4).ShouldBe("file never stored by the legacy upload queue");
        Reason(report, 5).ShouldBe("client not migrated or not a client");
        Reason(report, 6).ShouldStartWith("file refused by the document rules");
        report.Issues.ShouldContain(issue => issue.LegacyId == 2 && issue.Reason == "reference year missing: year of the upload");

        await using (var db = ImportHarness.Context(tenant))
        {
            var ids = await db.Set<LegacyIdMapping>().ToDictionaryAsync(row => (row.Entity, row.LegacyId), row => row.NewId, Ct);
            var (first, second) = (ids[("UserDocuments", 1)], ids[("UserDocuments", 2)]);
            var area = await db.Set<DocumentArea>().SingleAsync(Ct);
            area.Name.ShouldBe("Fiscale 2025");

            var document = await db.Set<Document>().SingleAsync(row => row.Id == first, Ct);
            (document.FileName, document.CaseId, document.FolderId, document.AreaId, document.UploadedByUserId, document.Status, document.Description)
                .ShouldBe(("730.pdf", (Guid?)ids[("Subscriptions", 1)], (Guid?)ids[("MembershipFolderTemplates", 2)], (Guid?)area.Id, (Guid?)ids[("Users", 2)],
                    DocumentStatus.Available, "Modello 730"));
            (document.Size, document.Sha256).ShouldBe((Pdf.Length, Convert.ToHexStringLower(SHA256.HashData(Pdf))));
            document.StorageKey.ShouldStartWith("tenants/t/documents/");
            store.Files[document.StorageKey].ShouldBe(Pdf);
            document.UploadedAt.ShouldBe(new DateTimeOffset(2025, 3, 5, 9, 0, 0, TimeSpan.Zero));

            var picture = await db.Set<Document>().SingleAsync(row => row.Id == second, Ct);
            (picture.FileName, picture.CaseId, picture.AreaId, picture.ReferenceYear).ShouldBe(("carta.jpg", (Guid?)null, (Guid?)null, 2025));
        }

        var again = await ImportHarness.ImportAsync(source, tenant, files: store, filesRoot: files);

        again.Tables["UserDocuments"].Created.ShouldBe(0);
        store.Files.Count.ShouldBe(2);
        await using (var db = ImportHarness.Context(tenant))
        {
            (await db.Set<Document>().CountAsync(Ct)).ShouldBe(2);
            (await db.Set<DocumentArea>().CountAsync(Ct)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Import_DryRun_ChecksTheFilesAndKeepsNone()
    {
        WriteFiles();
        await using var source = await LegacyAsync("legacy_documents_dry");
        await using var tenant = await fixture.CreateTenantAsync("tenant_documents_dry");
        var store = new MemoryFileStore();

        var report = await ImportHarness.ImportAsync(source, tenant, dryRun: true, files: store, filesRoot: files);

        report.Tables["UserDocuments"].Created.ShouldBe(2);
        store.Files.ShouldBeEmpty();
        await using var db = ImportHarness.Context(tenant);
        (await db.Set<Document>().CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Import_WithoutFilesDirectory_SkipsTheDocuments()
    {
        await using var source = await LegacyAsync("legacy_documents_nofiles");
        await using var tenant = await fixture.CreateTenantAsync("tenant_documents_nofiles");

        var report = await ImportHarness.ImportAsync(source, tenant);

        (report.Tables["UserDocuments"].Created, report.Tables["UserDocuments"].Skipped).ShouldBe((0, 6));
        Reason(report, 1).ShouldBe("no files directory given (--files)");
    }

    private static string Reason(LegacyImportReport report, int legacyId) =>
        report.Issues.Single(issue => issue.Table == "UserDocuments" && issue.LegacyId == legacyId && issue.Kind == LegacyIssueKind.Skipped).Reason;

    private void WriteFiles()
    {
        Directory.CreateDirectory(Path.Combine(files, "uploads", "documents"));
        File.WriteAllBytes(Path.Combine(files, "uploads", "documents", "0b6f1c2e-3f4a-4d5b-8c9d-0e1f2a3b4c5d_730.pdf"), Pdf);
        File.WriteAllBytes(Path.Combine(files, "1c7a2d3e-4f5a-4b6c-9d8e-1f2a3b4c5d6e_carta.jpg"), Jpeg);
        File.WriteAllBytes(Path.Combine(files, "uploads", "documents", "admin.pdf"), Pdf);
        File.WriteAllText(Path.Combine(files, "uploads", "documents", "note.txt"), "notes");
    }

    private async Task<LegacySource> LegacyAsync(string name)
    {
        var connection = await fixture.CreateLegacyAsync(
            name, "legacy-security-update.sql",
            LegacyDatabaseFixture.Script("users.sql") + LegacyDatabaseFixture.Script("cases.sql") + LegacyDatabaseFixture.Script("documents.sql"));
        return (await LegacySource.OpenAsync(connection, Ct)).Value;
    }
}
