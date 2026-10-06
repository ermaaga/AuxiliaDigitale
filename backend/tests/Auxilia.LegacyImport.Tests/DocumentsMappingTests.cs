using Auxilia.MigrationRunner.LegacyImport;
using Auxilia.MigrationRunner.LegacyImport.Steps;

namespace Auxilia.LegacyImport.Tests;

/// <summary>The file and area rules of E-04 (docs/migration/mapping.md §5.3), without databases.</summary>
public sealed class DocumentsMappingTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("legacy-root-").FullName;

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void Locate_FindsRelativePathsUrlsAndAbsolutePathsByName_NeverOutsideTheDirectory()
    {
        Directory.CreateDirectory(Path.Combine(root, "uploads", "documents"));
        File.WriteAllText(Path.Combine(root, "uploads", "documents", "a_scan.pdf"), "x");
        File.WriteAllText(Path.Combine(root, "b_blob.pdf"), "x");
        var outside = Path.Combine(Path.GetDirectoryName(root)!, $"outside-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(outside, "x");
        try
        {
            var files = new LegacyFiles(root);

            files.Locate("uploads/documents/a_scan.pdf").ShouldBe(Path.Combine(root, "uploads", "documents", "a_scan.pdf"));
            files.Locate("uploads\\documents\\a_scan.pdf").ShouldBe(Path.Combine(root, "uploads", "documents", "a_scan.pdf"));
            files.Locate("https://account.blob.core.windows.net/documents/b_blob.pdf").ShouldBe(Path.Combine(root, "b_blob.pdf"));
            files.Locate("ftp://host/documents/b_blob.pdf").ShouldBe(Path.Combine(root, "b_blob.pdf"));
            files.Locate("/app/uploads/b_blob.pdf").ShouldBe(Path.Combine(root, "b_blob.pdf"));
            files.Locate($"../{Path.GetFileName(outside)}").ShouldBeNull();
            files.Locate(outside).ShouldBeNull();
            files.Locate("queued").ShouldBeNull();
            new LegacyFiles(null).IsAvailable.ShouldBeFalse();
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Theory]
    [InlineData("0b6f1c2e-3f4a-4d5b-8c9d-0e1f2a3b4c5d_730.pdf", "730.pdf")]
    [InlineData("730.pdf", "730.pdf")]
    [InlineData("not-a-guid_730.pdf", "not-a-guid_730.pdf")]
    public void OriginalName_DropsTheLegacyPrefix(string legacy, string expected) => LegacyFiles.OriginalName(legacy).ShouldBe(expected);

    [Theory]
    [InlineData(" Fiscale   2025 ", "Fiscale 2025")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void AreaName_CollapsesSpaces(string? legacy, string? expected) => DocumentsStep.AreaName(legacy).ShouldBe(expected);
}
