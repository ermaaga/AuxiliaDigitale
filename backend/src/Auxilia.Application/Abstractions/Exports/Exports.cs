using System.Globalization;

using Auxilia.Diagnostics;
using Auxilia.Domain.Reporting;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Abstractions.Exports;

/// <summary>File formats of the exports (F26): CSV, Excel (ClosedXML), PDF (PDFsharp-MigraDoc).</summary>
public enum ExportFormat
{
    Csv,
    Xlsx,
    Pdf,
}

/// <summary>
/// A column a list can export: its id (the grid column id) and the translation key of its header; with
/// <paramref name="Translated"/> its values are translation keys too (statuses, types).
/// </summary>
public sealed record ExportColumn(string Id, string LabelKey, bool Translated = false);

/// <summary>A row of an export: the record id (selection of rows, F07) and the formatted value of each column.</summary>
public sealed record ExportRow(Guid Id, IReadOnlyDictionary<string, string> Values);

public sealed record ExportPage(IReadOnlyList<ExportRow> Rows, int Total);

/// <summary>How values are written: dates in the tenant time zone, numbers and dates in the reader's culture.</summary>
public sealed record ExportFormatting(TimeZoneInfo Zone, CultureInfo Culture)
{
    public string Date(DateOnly? value) => value is { } date ? date.ToString("d", Culture) : string.Empty;

    public string Date(DateTimeOffset? value) =>
        value is { } instant ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime).ToString("d", Culture) : string.Empty;

    public string DateTime(DateTimeOffset? value) =>
        value is { } instant ? TimeZoneInfo.ConvertTime(instant, Zone).DateTime.ToString("g", Culture) : string.Empty;

    public string Money(decimal value, string currency) => $"{value.ToString("N2", Culture)} {currency}";

    public string Number(long value) => value.ToString("N0", Culture);
}

/// <summary>
/// The filters of an export, the same query string as the list endpoint (<c>filter[…]</c>, <c>sort</c>, …). Values
/// that do not parse are field errors, never ignored (an export must not contain more than the list shows).
/// </summary>
public sealed class ExportParameters(IReadOnlyDictionary<string, string?> values)
{
    private readonly Dictionary<string, string[]> errors = new(StringComparer.Ordinal);

    public string? Text(string name) => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    public Guid? Id(string name) => Parse(name, text => System.Guid.TryParse(text, out var value) ? value : (Guid?)null);

    public bool? Bool(string name) => Parse(name, text => bool.TryParse(text, out var value) ? value : (bool?)null);

    public int? Number(string name) => Parse(name, text => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (int?)null);

    public DateOnly? Date(string name) =>
        Parse(name, text => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : (DateOnly?)null);

    public DateTimeOffset? Instant(string name) =>
        Parse(name, text => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : (DateTimeOffset?)null);

    /// <summary>The parse errors so far as a validation failure, or <c>null</c>.</summary>
    public Error? Errors => errors.Count == 0 ? null : Diagnostics.Errors.Host.ValidationFailed(errors);

    private T? Parse<T>(string name, Func<string, T?> parse)
        where T : struct
    {
        if (Text(name) is not { } text)
        {
            return null;
        }

        var value = parse(text);
        if (value is null)
        {
            errors[name] = ["validation.exports.parameter"];
        }

        return value;
    }
}

/// <summary>
/// A list that can be exported (F26), registered by its module: it pages the same query service as the list
/// endpoint, so permissions, F10 and every filter apply exactly as on screen.
/// </summary>
public interface IExportSource
{
    /// <summary>The route segment, e.g. <c>clients</c>.</summary>
    string Key { get; }

    /// <summary>Translation key of the document title (e.g. <c>nav.clients</c>).</summary>
    string TitleKey { get; }

    /// <summary>The permission of the list.</summary>
    string Permission { get; }

    IReadOnlyList<ExportColumn> Columns { get; }

    Task<Result<ExportPage>> PageAsync(ExportParameters parameters, int page, int pageSize, ExportFormatting formatting, CancellationToken cancellationToken);
}

/// <summary>A document to write: title, subtitle (tenant and generation time), headers, rows and the total line.</summary>
public sealed record ExportTable(string Title, string Subtitle, string Footer, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, string PageLabel, string OfLabel);

/// <summary>Writes an <see cref="ExportTable"/> in one format (Infrastructure adapters).</summary>
public interface IExportWriter
{
    ExportFormat Format { get; }

    string ContentType { get; }

    string Extension { get; }

    byte[] Write(ExportTable table);
}

/// <summary>A queued export without its file content (lists).</summary>
public sealed record ExportJobRow(
    Guid Id, string SourceKey, string Format, ExportJobStatus Status, string? FileName, int? RowCount, string? ErrorCode, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>The queued exports of the current tenant (F26), one unit of work.</summary>
public interface IExportJobData : IAsyncDisposable
{
    Task<ExportJob?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The user's exports not expired yet, newest first, without content.</summary>
    Task<IReadOnlyList<ExportJobRow>> OfUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Deletes the expired exports (no timers, D-15: done whenever a new one is queued).</summary>
    Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);

    void Add(ExportJob job);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IExportJobDataFactory
{
    Task<IExportJobData> OpenAsync(CancellationToken cancellationToken);
}
