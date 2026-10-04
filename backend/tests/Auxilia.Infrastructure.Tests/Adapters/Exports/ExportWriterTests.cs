using System.Text;

using Auxilia.Application.Abstractions.Exports;
using Auxilia.Infrastructure.Adapters.Exports;

using ClosedXML.Excel;

namespace Auxilia.Infrastructure.Tests.Adapters.Exports;

public sealed class ExportWriterTests
{
    private static readonly ExportTable Table = new(
        "demo - Clienti",
        "demo - generato il 04/10/2026 10:00",
        "Totale: 2",
        ["Cognome", "Nome", "Note"],
        [["Rossi", "Mario", "=1+1"], ["Bianchi", "Anna \"Annina\"", "àèìòù;€"]],
        "Pagina",
        "di");

    [Fact]
    public void Csv_IsUtf8WithBom_QuotedSemicolonSeparated_AndNeutralisesFormulas()
    {
        var writer = new CsvExportWriter();

        var bytes = writer.Write(Table);

        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());
        var text = Encoding.UTF8.GetString(bytes[3..]);
        text.ShouldBe("\"Cognome\";\"Nome\";\"Note\"\r\n\"Rossi\";\"Mario\";\"'=1+1\"\r\n\"Bianchi\";\"Anna \"\"Annina\"\"\";\"àèìòù;€\"\r\n");
        (writer.Format, writer.Extension).ShouldBe((ExportFormat.Csv, "csv"));
        CsvExportWriter.Safe("-5").ShouldBe("'-5");
        CsvExportWriter.Safe("Rossi").ShouldBe("Rossi");
    }

    [Fact]
    public void Xlsx_HasTitle_HeaderAndRowsAsText()
    {
        var bytes = new XlsxExportWriter().Write(Table);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheets.Single();
        sheet.Name.ShouldBe("demo - Clienti");
        sheet.Cell(1, 1).GetString().ShouldBe("demo - Clienti");
        sheet.Cell(4, 1).GetString().ShouldBe("Cognome");
        sheet.Cell(5, 3).GetString().ShouldBe("=1+1");
        sheet.Cell(5, 3).HasFormula.ShouldBeFalse();
        sheet.Cell(6, 2).GetString().ShouldBe("Anna \"Annina\"");
        sheet.Cell(8, 1).GetString().ShouldBe("Totale: 2");
    }

    [Fact]
    public void Pdf_IsAPdfDocument_WithTheEmbeddedFont()
    {
        var bytes = new PdfExportWriter().Write(Table with { Rows = Enumerable.Range(0, 120).Select(index => (IReadOnlyList<string>)[$"Rossi {index}", "Mario", "nota"]).ToArray() });

        Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");
        // More than one page: the header row repeats and the footer counts them.
        using var document = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(bytes), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        document.PageCount.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Pdf_EmptyTable_StillRenders()
    {
        var bytes = new PdfExportWriter().Write(Table with { Headers = [], Rows = [] });

        Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");
    }
}
