using Auxilia.Application.Abstractions.Exports;

using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

using PdfSharp.Fonts;

namespace Auxilia.Infrastructure.Adapters.Exports;

/// <summary>
/// PDF (PDFsharp-MigraDoc, F26 and the client overview F07): A4 landscape when the table is wide, title "{app} -
/// {title}" with the generation date, a header row repeated on every page, the total, "page X of Y" in the footer.
/// </summary>
internal sealed class PdfExportWriter : IExportWriter
{
    private const string FontName = ExportFontResolver.FamilyName;

    static PdfExportWriter() => ExportFontResolver.Register();

    public ExportFormat Format => ExportFormat.Pdf;

    public string ContentType => "application/pdf";

    public string Extension => "pdf";

    public byte[] Write(ExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var document = new Document();
        document.Info.Title = table.Title;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = FontName;
        normal.Font.Size = 8;

        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = table.Headers.Count > 5 ? Orientation.Landscape : Orientation.Portrait;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);

        var title = section.AddParagraph(table.Title);
        title.Format.Font.Size = 14;
        title.Format.Font.Bold = true;
        var subtitle = section.AddParagraph(table.Subtitle);
        subtitle.Format.SpaceAfter = Unit.FromPoint(8);

        var width = (section.PageSetup.Orientation == Orientation.Landscape ? 29.7 : 21.0) - 3.0;
        if (table.Headers.Count > 0)
        {
            AddGrid(section, table, width);
        }

        var total = section.AddParagraph(table.Footer);
        total.Format.SpaceBefore = Unit.FromPoint(8);
        total.Format.Font.Bold = true;

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Alignment = ParagraphAlignment.Right;
        footer.AddText(table.PageLabel + " ");
        footer.AddPageField();
        footer.AddText(" " + table.OfLabel + " ");
        footer.AddNumPagesField();

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    /// <summary>The table: equal columns over the page width, the header row repeated on every page.</summary>
    private static void AddGrid(Section section, ExportTable table, double width)
    {
        var grid = section.AddTable();
        grid.Borders.Width = 0.25;
        grid.Borders.Color = Colors.Gray;
        foreach (var _ in table.Headers)
        {
            grid.AddColumn(Unit.FromCentimeter(width / table.Headers.Count));
        }

        var header = grid.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        header.Shading.Color = Colors.LightGray;
        for (var column = 0; column < table.Headers.Count; column++)
        {
            header.Cells[column].AddParagraph(table.Headers[column]);
        }

        foreach (var values in table.Rows)
        {
            var row = grid.AddRow();
            for (var column = 0; column < table.Headers.Count && column < values.Count; column++)
            {
                row.Cells[column].AddParagraph(values[column]);
            }
        }
    }
}

/// <summary>
/// Fonts for PDFsharp on every platform (Linux containers have no system fonts): the Segoe WP family shipped with
/// PDFsharp (<c>PdfSharp.WPFonts</c>, MIT package), regular and bold.
/// </summary>
internal sealed class ExportFontResolver : IFontResolver
{
    public const string FamilyName = "Auxilia Export";

    private static readonly Lock Gate = new();

    public static void Register()
    {
        lock (Gate)
        {
            if (GlobalFontSettings.FontResolver is not ExportFontResolver)
            {
                GlobalFontSettings.FontResolver = new ExportFontResolver();
            }
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? "segoe-wp-bold" : "segoe-wp", mustSimulateBold: false, mustSimulateItalic: italic);

    public byte[]? GetFont(string faceName) =>
        faceName == "segoe-wp-bold" ? PdfSharp.WPFonts.FontDataHelper.SegoeWPBold : PdfSharp.WPFonts.FontDataHelper.SegoeWP;
}
