using System.Text;

using Auxilia.Application.Abstractions.Exports;

namespace Auxilia.Infrastructure.Adapters.Exports;

/// <summary>
/// CSV (legacy export, F26): UTF-8 with BOM (Excel opens it with accents), every value quoted, semicolon separator
/// (the legacy's European Excel reads it as columns), CRLF lines. Values starting with a formula character get a
/// leading apostrophe (CSV injection).
/// </summary>
internal sealed class CsvExportWriter : IExportWriter
{
    public ExportFormat Format => ExportFormat.Csv;

    public string ContentType => "text/csv; charset=utf-8";

    public string Extension => "csv";

    public byte[] Write(ExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var text = new StringBuilder();
        Line(text, table.Headers);
        foreach (var row in table.Rows)
        {
            Line(text, row);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    /// <summary>Neutralises spreadsheet formulas in a value (OWASP CSV injection).</summary>
    public static string Safe(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    private static void Line(StringBuilder text, IReadOnlyList<string> values)
    {
        text.AppendJoin(';', values.Select(value => "\"" + Safe(value).Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));
        text.Append("\r\n");
    }
}
