using System.Globalization;
using System.IO.Compression;

using Auxilia.Application.Abstractions.Imports;

using ClosedXML.Excel;

namespace Auxilia.Infrastructure.Adapters.Imports;

/// <summary>Import workbooks with ClosedXML (F19): first worksheet, header row of field keys, one record per non-empty row.</summary>
internal sealed class XlsxImportWorkbook : IImportWorkbook
{
    /// <summary>Excel sheet names are at most 31 characters, without <c>[]:*?/\</c>.</summary>
    private const int SheetNameMaxLength = 31;

    /// <summary>
    /// Limits of the package before ClosedXML opens it (H-01, zip bomb): a 10 MB upload of 5000 rows needs far less.
    /// .NET refuses an entry that inflates beyond its declared size, so the declared sizes are a real bound.
    /// </summary>
    internal const long MaxUncompressedBytes = 200L * 1024 * 1024;

    internal const int MaxEntries = 1000;

    public ImportSheet? Read(byte[] content, IReadOnlyList<ImportField> fields)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(fields);

        if (!IsReasonablePackage(content))
        {
            return null;
        }

        XLWorkbook workbook;
        try
        {
            using var stream = new MemoryStream(content, writable: false);
            workbook = new XLWorkbook(stream);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault();
            if (sheet?.LastCellUsed() is not { } last)
            {
                return new ImportSheet([], [.. fields.Where(field => field.Required).Select(field => field.Key)]);
            }

            var keys = fields.ToDictionary(field => field.Key, field => field.Key, StringComparer.OrdinalIgnoreCase);
            var columns = new Dictionary<int, string>();
            var lastColumn = last.Address.ColumnNumber;
            for (var column = 1; column <= lastColumn; column++)
            {
                if (keys.TryGetValue(sheet.Cell(1, column).GetString().Trim(), out var key) && !columns.ContainsValue(key))
                {
                    columns[column] = key;
                }
            }

            var missing = fields.Where(field => field.Required && !columns.ContainsValue(field.Key)).Select(field => field.Key).ToArray();
            var rows = new List<ImportRow>();
            var lastRow = last.Address.RowNumber;
            for (var row = 2; row <= lastRow; row++)
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (column, key) in columns)
                {
                    if (Text(sheet.Cell(row, column)) is { Length: > 0 } text)
                    {
                        values[key] = text;
                    }
                }

                if (values.Count > 0)
                {
                    rows.Add(new ImportRow(row, values));
                }
            }

            return new ImportSheet(rows, missing);
        }
    }

    /// <summary>A ZIP package (signature <c>PK\x03\x04</c>) with a bounded number of entries and uncompressed size.</summary>
    internal static bool IsReasonablePackage(byte[] content)
    {
        if (content.Length < 4 || content[0] != 0x50 || content[1] != 0x4B || content[2] != 0x03 || content[3] != 0x04)
        {
            return false;
        }

        try
        {
            using var archive = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
            return archive.Entries.Count <= MaxEntries && archive.Entries.Sum(entry => entry.Length) <= MaxUncompressedBytes;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    public byte[] Template(string sheetName, IReadOnlyList<ImportField> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(sheetName));
        for (var column = 0; column < fields.Count; column++)
        {
            var cell = sheet.Cell(1, column + 1);
            cell.Value = fields[column].Key;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = fields[column].Required ? XLColor.FromHtml("#F4B6B6") : XLColor.FromHtml("#EEEEEE");
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, Math.Max(1, fields.Count)).Width = 22;
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static string Text(IXLCell cell)
    {
        var value = cell.Value;
        if (value.IsDateTime)
        {
            var date = value.GetDateTime();
            return date.TimeOfDay == TimeSpan.Zero
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : date.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        }

        if (value.IsNumber)
        {
            return value.GetNumber().ToString(CultureInfo.InvariantCulture);
        }

        if (value.IsBoolean)
        {
            return value.GetBoolean() ? "true" : "false";
        }

        return value.IsText ? value.GetText().Trim() : string.Empty;
    }

    private static string SheetName(string name)
    {
        var clean = new string([.. name.Where(character => "[]:*?/\\".IndexOf(character, StringComparison.Ordinal) < 0)]).Trim();
        clean = clean.Length == 0 ? "Import" : clean;
        return clean.Length > SheetNameMaxLength ? clean[..SheetNameMaxLength] : clean;
    }
}
