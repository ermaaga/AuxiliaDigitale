using System.Security.Cryptography;
using System.Text;

using Auxilia.Application.Abstractions.Storage;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Documents;
using Auxilia.Application.Documents.Public;
using Auxilia.Application.Tests.Directory;
using Auxilia.Application.Tests.Identity;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

namespace Auxilia.Application.Tests.Documents;

public sealed class FileStoreTests
{
    private static readonly byte[] Pdf = [.. "%PDF-1.7\n"u8, .. new byte[1000]];

    private readonly MemoryStorage storage = new();
    private readonly FakeSettings settings = new();
    private readonly FileStore store;

    public FileStoreTests()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.Tenant.Returns(new TenantInfo(Guid.CreateVersion7(), "demo", TenantStatus.Active, "it", "Europe/Rome"));
        store = new FileStore([storage], settings, tenant, new ManualTimeProvider(), NullLogger<FileStore>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<Result<StagedFile>> StageAsync(byte[] content, string name) => store.StageAsync(new MemoryStream(content), name, Ct);

    [Fact]
    public async Task Stage_ChecksAndHashes_AndWritesToTheTenantsStaging()
    {
        var staged = (await StageAsync(Pdf, "my file's report.PDF")).Value;

        (staged.FileName, staged.Extension, staged.ContentType, staged.Size).ShouldBe(("my_files_report.PDF", "pdf", "application/pdf", Pdf.Length));
        staged.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(Pdf)));
        staged.StagingKey.ShouldStartWith("tenants/demo/staging/");
        storage.Files[staged.StagingKey].ShouldBe(Pdf);
    }

    [Fact]
    public async Task Commit_MovesToTheAreaByMonth()
    {
        var staged = (await StageAsync(Pdf, "a.pdf")).Value;

        var key = (await store.CommitAsync(staged, "documents", Ct)).Value;

        key.ShouldMatch(@"^tenants/demo/documents/2026/09/[0-9a-f]{32}\.pdf$");
        storage.Files.Keys.ShouldBe([key]);
        await Should.ThrowAsync<ArgumentException>(() => store.CommitAsync(staged, "staging", Ct));
        (await store.CommitAsync(staged with { StagingKey = "tenants/other/staging/x" }, "documents", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("script.js")]
    [InlineData("noextension")]
    public async Task Stage_TypesOutsideTheWhitelist_AreRefused(string name)
    {
        var result = await StageAsync(Pdf, name);

        result.Error!.Code.ShouldBeOneOf(EventCodes.Documents.FileTypeNotAllowed, EventCodes.Documents.FileInvalid);
        storage.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stage_ContentNotMatchingTheType_IsRefused()
    {
        (await StageAsync("MZ\u0090\0binary"u8.ToArray(), "invoice.pdf")).Error!.Code.ShouldBe(EventCodes.Documents.FileContentMismatch);
        (await StageAsync([0x41, 0x00, 0x42], "notes.txt")).Error!.Code.ShouldBe(EventCodes.Documents.FileContentMismatch);
        (await StageAsync(Encoding.UTF8.GetBytes("name;surname\nMario;Rossi"), "list.csv")).IsSuccess.ShouldBeTrue();
        (await StageAsync([], "empty.pdf")).Error!.Code.ShouldBe(EventCodes.Documents.FileInvalid);
        (await StageAsync(Pdf, ".pdf")).Error!.Code.ShouldBe(EventCodes.Documents.FileInvalid);
    }

    [Fact]
    public async Task Stage_TooLarge_IsRefused_AndLeavesNothing()
    {
        settings.Set(DocumentsSettings.MaxUploadMb, 1);

        var result = await StageAsync([.. "%PDF-"u8, .. new byte[(1024 * 1024) + 1]], "big.pdf");

        result.Error!.Code.ShouldBe(EventCodes.Documents.FileTooLarge);
        storage.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task KeysOfOtherTenants_OrWithParentSegments_AreRefused()
    {
        var staged = (await StageAsync(Pdf, "a.pdf")).Value;

        (await store.OpenReadAsync(staged.StagingKey, Ct)).Value.ShouldNotBeNull();
        (await store.OpenReadAsync("tenants/demo/staging/missing", Ct)).Value.ShouldBeNull();
        (await store.OpenReadAsync("tenants/other/documents/x.pdf", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
        (await store.DeleteAsync("tenants/demo/../other/x.pdf", Ct)).Error!.Code.ShouldBe(EventCodes.Identity.PermissionDenied);
        (await store.DeleteAsync(staged.StagingKey, Ct)).IsSuccess.ShouldBeTrue();
        storage.Files.ShouldBeEmpty();
    }

    [Fact]
    public async Task ProviderWithoutConfiguration_IsUnavailable()
    {
        settings.Set(DocumentsSettings.StorageProvider, "ftp");

        (await StageAsync(Pdf, "a.pdf")).Error!.Code.ShouldBe(EventCodes.Documents.StorageUnavailable);
    }

    [Fact]
    public async Task StorageFailure_IsUnavailable()
    {
        storage.Fail = true;

        (await StageAsync(Pdf, "a.pdf")).Error!.Code.ShouldBe(EventCodes.Documents.StorageUnavailable);
        (await store.OpenReadAsync("tenants/demo/documents/a.pdf", Ct)).Error!.Code.ShouldBe(EventCodes.Documents.StorageUnavailable);
        (await store.DeleteAsync("tenants/demo/documents/a.pdf", Ct)).Error!.Code.ShouldBe(EventCodes.Documents.StorageUnavailable);
    }

    // Legacy DocumentManagerTests (Q51): the same names come out.
    [Theory]
    [InlineData("my file.pdf", "my_file.pdf")]
    [InlineData("o'leary's report.pdf", "olearys_report.pdf")]
    [InlineData("file \"name\".pdf", "file_name.pdf")]
    [InlineData("docs \"final'\".txt", "docs_final.txt")]
    [InlineData("report-2025_final.pdf", "report-2025_final.pdf")]
    [InlineData("C:\\Users\\me\\scan:1.pdf", "scan1.pdf")]
    [InlineData("  ", "")]
    public void Sanitize_FollowsTheLegacyRules(string input, string expected) => FileNames.Sanitize(input).ShouldBe(expected);

    [Fact]
    public void Sanitize_LongNames_KeepTheExtension()
    {
        var name = FileNames.Sanitize(new string('a', 300) + ".docx");

        (name.Length, name.EndsWith(".docx", StringComparison.Ordinal)).ShouldBe((FileNames.MaxLength, true));
    }

    [Theory]
    [InlineData("png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, true)]
    [InlineData("jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData("docx", new byte[] { 0x50, 0x4B, 0x03, 0x04 }, true)]
    [InlineData("doc", new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }, true)]
    [InlineData("tif", new byte[] { 0x49, 0x49, 0x2A, 0x00 }, true)]
    [InlineData("p7m", new byte[] { 0x30, 0x82 }, true)]
    [InlineData("webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, true)]
    [InlineData("png", new byte[] { 0xFF, 0xD8, 0xFF }, false)]
    [InlineData("xlsx", new byte[] { 0x25, 0x50, 0x44, 0x46 }, false)]
    public void MagicBytes_RecogniseTheTypes(string extension, byte[] header, bool matches) =>
        FileTypes.Find(extension)!.Matches(header).ShouldBe(matches);
}

/// <summary>A storage in memory (provider <c>local</c>).</summary>
internal sealed class MemoryStorage : IFileStorage
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public bool Fail { get; set; }

    public string Provider => "local";

    public bool IsConfigured(StorageTarget target) => true;

    public async Task WriteAsync(StorageTarget target, string key, Stream content, CancellationToken cancellationToken)
    {
        Check();
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        Files[key] = buffer.ToArray();
    }

    public Task<Stream?> OpenReadAsync(StorageTarget target, string key, CancellationToken cancellationToken)
    {
        Check();
        return Task.FromResult<Stream?>(Files.TryGetValue(key, out var content) ? new MemoryStream(content) : null);
    }

    public Task MoveAsync(StorageTarget target, string sourceKey, string destinationKey, CancellationToken cancellationToken)
    {
        Check();
        Files[destinationKey] = Files[sourceKey];
        Files.Remove(sourceKey);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(StorageTarget target, string key, CancellationToken cancellationToken)
    {
        Check();
        Files.Remove(key);
        return Task.CompletedTask;
    }

    private void Check()
    {
        if (Fail)
        {
            throw new IOException("Storage down");
        }
    }
}
