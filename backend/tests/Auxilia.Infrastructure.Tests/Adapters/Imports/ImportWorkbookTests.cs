using System.IO.Compression;

using Auxilia.Application.Abstractions.Imports;
using Auxilia.Infrastructure.Adapters.Imports;

using ClosedXML.Excel;

namespace Auxilia.Infrastructure.Tests.Adapters.Imports;

public sealed class ImportWorkbookTests
{
    private static readonly ImportField[] Fields =
    [
        new("firstName", "FirstName", Required: true),
        new("birthDate", "BirthDate", Required: true),
        new("price", "Price", Required: false),
    ];

    private readonly XlsxImportWorkbook workbook = new();

    [Fact]
    public void Template_HasTheKeysAsHeaders_RequiredOnesRed()
    {
        using var stream = new MemoryStream(workbook.Template("Clienti [2026]", Fields));
        using var book = new XLWorkbook(stream);
        var sheet = book.Worksheets.Single();

        sheet.Name.ShouldBe("Clienti 2026");
        (sheet.Cell(1, 1).GetString(), sheet.Cell(1, 2).GetString(), sheet.Cell(1, 3).GetString()).ShouldBe(("firstName", "birthDate", "price"));
        sheet.Cell(1, 1).Style.Fill.BackgroundColor.ShouldBe(XLColor.FromHtml("#F4B6B6"));
        sheet.Cell(1, 3).Style.Fill.BackgroundColor.ShouldBe(XLColor.FromHtml("#EEEEEE"));
    }

    [Fact]
    public void Read_MapsColumnsByKey_ConvertsDatesAndNumbers_AndSkipsEmptyRows()
    {
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add("Data");
        sheet.Cell(1, 1).Value = "BIRTHDATE";
        sheet.Cell(1, 2).Value = "unknown";
        sheet.Cell(1, 3).Value = "firstName";
        sheet.Cell(1, 4).Value = "price";
        sheet.Cell(2, 1).Value = new DateTime(1980, 1, 31, 0, 0, 0, DateTimeKind.Unspecified);
        sheet.Cell(2, 2).Value = "ignored";
        sheet.Cell(2, 3).Value = "  Mario ";
        sheet.Cell(2, 4).Value = 12.5;
        sheet.Cell(4, 3).Value = "Anna";
        using var output = new MemoryStream();
        book.SaveAs(output);

        var read = workbook.Read(output.ToArray(), Fields)!;

        read.MissingColumns.ShouldBeEmpty();
        read.Rows.Select(row => row.RowNumber).ShouldBe([2, 4]);
        read.Rows[0].Values.ShouldBe(new Dictionary<string, string> { ["birthDate"] = "1980-01-31", ["firstName"] = "Mario", ["price"] = "12.5" });
        read.Rows[1].Values.ShouldBe(new Dictionary<string, string> { ["firstName"] = "Anna" });
    }

    [Fact]
    public void Read_ReportsMissingRequiredColumns_AndRejectsNonWorkbooks()
    {
        using var book = new XLWorkbook();
        book.Worksheets.Add("Data").Cell(1, 1).Value = "price";
        using var output = new MemoryStream();
        book.SaveAs(output);

        workbook.Read(output.ToArray(), Fields)!.MissingColumns.ShouldBe(["firstName", "birthDate"]);
        workbook.Read("a;b;c"u8.ToArray(), Fields).ShouldBeNull();
    }

    [Fact]
    public void Read_RejectsPackagesThatInflateBeyondTheLimit()
    {
        // A zip bomb: one entry of highly compressible zeros whose declared size is above the limit.
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.SmallestSize).Open();
            var zeros = new byte[1024 * 1024];
            for (var written = 0L; written <= XlsxImportWorkbook.MaxUncompressedBytes; written += zeros.Length)
            {
                entry.Write(zeros);
            }
        }

        var content = output.ToArray();
        content.Length.ShouldBeLessThan(1024 * 1024);
        XlsxImportWorkbook.IsReasonablePackage(content).ShouldBeFalse();
        workbook.Read(content, Fields).ShouldBeNull();
    }
}
