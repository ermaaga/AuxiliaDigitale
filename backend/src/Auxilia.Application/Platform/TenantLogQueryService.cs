using System.Globalization;
using System.Text.Json;

using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Contracts.Platform;
using Auxilia.Diagnostics;
using Auxilia.Diagnostics.Logging;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Platform;

/// <summary>
/// Reads of the console Log page (F25, D-17): the tenant's log level and a search of its daily files, newest first.
/// Nothing is copied to a database; the files are read where the pipeline writes them.
/// </summary>
public interface ITenantLogQueryService
{
    Task<Result<TenantLogLevelResponse>> GetLevelAsync(string slug, CancellationToken cancellationToken);

    Task<Result<TenantLogPageResponse>> SearchAsync(string slug, TenantLogQuery query, CancellationToken cancellationToken);
}

internal sealed class TenantLogQueryService(
    ICatalogStore catalog,
    ITenantLogLevels levels,
    ILogFileReader files,
    TimeProvider timeProvider,
    ILogger<TenantLogQueryService> logger) : ITenantLogQueryService
{
    /// <summary>Days one search may span (a month of files).</summary>
    public const int MaxDays = 31;

    public const int DefaultPageSize = 100;

    public const int MaxPageSize = 200;

    /// <summary>Serilog levels from the lowest; a line without <c>@l</c> is Information (CLEF).</summary>
    private static readonly string[] Levels = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    /// <summary>CLEF fields and properties shown in their own columns (the rest goes to <c>properties</c>).</summary>
    private static readonly HashSet<string> Columns = new(StringComparer.Ordinal)
    {
        "@t", "@l", "@m", "@mt", "@x", "@i", "@r", "@tr", "@sp", "EventCode", "EventId", "UserId", "Operation", "SourceContext", "TenantSlug",
    };

    public async Task<Result<TenantLogLevelResponse>> GetLevelAsync(string slug, CancellationToken cancellationToken)
    {
        if (await catalog.FindTenantAsync(slug, cancellationToken) is not { } tenant)
        {
            return Errors.Tenancy.TenantNotFound();
        }

        return new TenantLogLevelResponse(levels.DefaultLevel, tenant.ActiveDebugLoggingUntil(timeProvider.GetUtcNow()));
    }

    public async Task<Result<TenantLogPageResponse>> SearchAsync(string slug, TenantLogQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (await catalog.FindTenantAsync(slug, cancellationToken) is not { } tenant)
        {
            return Errors.Tenancy.TenantNotFound();
        }

        var search = Parse(query, DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
        if (search.IsFailure)
        {
            return Result.Failure<TenantLogPageResponse>(search.Error!);
        }

        try
        {
            return await ReadAsync(tenant.Slug, search.Value, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Storage errors (I/O, blob service) become a coded failure (ADR 0012: catch to add meaning).
            Log.Tenancy.LogFilesUnavailable(logger, exception, tenant.Slug);
            return Errors.Tenancy.LogFilesUnavailable();
        }
    }

    private async Task<TenantLogPageResponse> ReadAsync(string slug, Search search, CancellationToken cancellationToken)
    {
        var page = new List<(DateOnly Day, int Line, TenantLogEntryResponse Entry)>(search.PageSize);
        var day = search.Cursor?.Day ?? search.To;
        var before = search.Cursor?.Line ?? int.MaxValue;

        while (day >= search.From && page.Count < search.PageSize)
        {
            // The newest matches of the day: keep only as many as the page still needs.
            var needed = search.PageSize - page.Count;
            var newest = new Queue<(int Line, TenantLogEntryResponse Entry)>(needed + 1);
            var line = 0;
            await foreach (var text in files.ReadTenantDayAsync(slug, day, cancellationToken))
            {
                if (line >= before)
                {
                    break;
                }

                if (TryParse(text) is { } entry && search.Matches(entry))
                {
                    newest.Enqueue((line, entry));
                    if (newest.Count > needed)
                    {
                        newest.Dequeue();
                    }
                }

                line++;
            }

            page.AddRange(newest.Reverse().Select(item => (day, item.Line, item.Entry)));
            day = day.AddDays(-1);
            before = int.MaxValue;
        }

        // A full page may be followed by older events; the cursor points at the last one returned.
        var next = page.Count == search.PageSize && page[^1] is var last
            ? new Cursor(last.Day, last.Line).ToString()
            : null;
        return new TenantLogPageResponse(page.Select(item => item.Entry).ToArray(), next);
    }

    private static Result<Search> Parse(TenantLogQuery query, DateOnly today)
    {
        var to = query.To ?? today;
        var from = query.From ?? to;
        if (from > to || to.DayNumber - from.DayNumber >= MaxDays)
        {
            return Errors.Tenancy.LogQueryInvalid("from", "validation.logs.range");
        }

        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            return Errors.Tenancy.LogQueryInvalid("pageSize", "validation.logs.pageSize");
        }

        var minimumLevel = 0;
        if (!string.IsNullOrWhiteSpace(query.Level))
        {
            minimumLevel = Array.FindIndex(Levels, level => string.Equals(level, query.Level.Trim(), StringComparison.OrdinalIgnoreCase));
            if (minimumLevel < 0)
            {
                return Errors.Tenancy.LogQueryInvalid("level", "validation.logs.level");
            }
        }

        Cursor? cursor = null;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            if (!Cursor.TryParse(query.Cursor, out var parsed) || parsed.Day < from || parsed.Day > to)
            {
                return Errors.Tenancy.LogQueryInvalid("cursor", "validation.logs.cursor");
            }

            cursor = parsed;
        }

        return new Search(from, to, minimumLevel, NormalizeCode(query.Code), Trimmed(query.TraceId), Trimmed(query.UserId), Trimmed(query.Text), cursor, pageSize);
    }

    /// <summary><c>11001</c>, <c>aux-11001</c> and <c>AUX-11001</c> all search <c>AUX-11001</c>.</summary>
    private static string? NormalizeCode(string? code)
    {
        var value = Trimmed(code);
        return value is null ? null : value.StartsWith("AUX-", StringComparison.OrdinalIgnoreCase) ? "AUX-" + value[4..] : "AUX-" + value;
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>One CLEF line (compact JSON with rendered message); <c>null</c> when it is not one (e.g. still being appended).</summary>
    internal static TenantLogEntryResponse? TryParse(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("@t", out var time) || !time.TryGetDateTimeOffset(out var timestamp))
            {
                return null;
            }

            var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject().Where(property => !Columns.Contains(property.Name)))
            {
                properties[property.Name] = Text(property.Value);
            }

            return new TenantLogEntryResponse(
                timestamp,
                String(root, "@l") ?? "Information",
                String(root, "EventCode"),
                String(root, "@m") ?? String(root, "@mt") ?? string.Empty,
                String(root, "@x"),
                String(root, "@tr"),
                String(root, "UserId"),
                String(root, "Operation"),
                String(root, "SourceContext"),
                properties);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? String(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? Text(value) : null;

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    private sealed record Search(
        DateOnly From, DateOnly To, int MinimumLevel, string? Code, string? TraceId, string? UserId, string? Text, Cursor? Cursor, int PageSize)
    {
        public bool Matches(TenantLogEntryResponse entry) =>
            Array.FindIndex(Levels, level => string.Equals(level, entry.Level, StringComparison.Ordinal)) >= MinimumLevel
            && (Code is null || string.Equals(entry.EventCode, Code, StringComparison.OrdinalIgnoreCase))
            && (TraceId is null || string.Equals(entry.TraceId, TraceId, StringComparison.OrdinalIgnoreCase))
            && (UserId is null || string.Equals(entry.UserId, UserId, StringComparison.OrdinalIgnoreCase))
            && (Text is null
                || entry.Message.Contains(Text, StringComparison.OrdinalIgnoreCase)
                || entry.Exception?.Contains(Text, StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary><c>yyyyMMdd:line</c>: the next page has the events of that day before that line, then older days.</summary>
    private sealed record Cursor(DateOnly Day, int Line)
    {
        public static bool TryParse(string value, out Cursor cursor)
        {
            cursor = null!;
            var parts = value.Split(':');
            if (parts.Length != 2
                || !DateOnly.TryParseExact(parts[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var line))
            {
                return false;
            }

            cursor = new Cursor(day, line);
            return true;
        }

        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Day:yyyyMMdd}:{Line}");
    }
}
