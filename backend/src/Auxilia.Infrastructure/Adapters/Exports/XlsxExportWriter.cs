using Auxilia.Application.Abstractions.Exports;

using ClosedXML.Excel;

namespace Auxilia.Infrastructure.Adapters.Exports;

/// <summary>Excel (ClosedXML, F26): title and subtitle, bold frozen header row with a filter, every value as text, the total line.</summary>
internal sealed class XlsxExportWriter : IExportWriter
{
    /// <summary>Excel sheet names are at most 31 characters, without <c>[]:*?/\</c>.</summary>
    private const int SheetNameMaxLength = 31;

    public ExportFormat Format => ExportFormat.Xlsx;

    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public string Extension => "xlsx";

    public byte[] Write(ExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(table.Title));
        sheet.Cell(1, 1).Value = table.Title;
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value = table.Subtitle;

        const int headerRow = 4;
        for (var column = 0; column < table.Headers.Count; column++)
        {
            var cell = sheet.Cell(headerRow, column + 1);
            cell.Value = table.Headers[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEEEEE");
        }

        for (var row = 0; row < table.Rows.Count; row++)
        {
            for (var column = 0; column < table.Rows[row].Count; column++)
            {
                // Text only: values are already formatted for the reader, and text cannot run as a formula.
                var cell = sheet.Cell(headerRow + 1 + row, column + 1);
                cell.SetValue(table.Rows[row][column]);
                cell.Style.NumberFormat.Format = "@";
            }
        }

        if (table.Headers.Count > 0)
        {
            sheet.Range(headerRow, 1, headerRow + table.Rows.Count, table.Headers.Count).SetAutoFilter();
        }

        sheet.SheetView.FreezeRows(headerRow);
        sheet.Cell(headerRow + table.Rows.Count + 2, 1).Value = table.Footer;
        sheet.Columns().AdjustToContents(headerRow, headerRow + Math.Min(table.Rows.Count, 200), 10, 60);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string SheetName(string title)
    {
        var clean = new string(title.Where(character => character is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\')).ToArray()).Trim();
        clean = clean.Length == 0 ? "Export" : clean;
        return clean.Length > SheetNameMaxLength ? clean[..SheetNameMaxLength] : clean;
    }
}
