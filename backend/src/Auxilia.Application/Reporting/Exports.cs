using System.Globalization;
using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Exports;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Engagement.Public;
using Auxilia.Application.Localization.Public;
using Auxilia.Contracts.Messages.V1.Reporting;
using Auxilia.Contracts.Realtime;
using Auxilia.Contracts.Reporting;
using Auxilia.Diagnostics;
using Auxilia.Domain.Reporting;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Reporting;

/// <summary>
/// An export request (F26, F07): the list, <c>format</c> (<c>csv</c>, <c>xlsx</c>, <c>pdf</c>), the <c>columns</c>
/// shown (comma separated ids, default all), the <c>ids</c> of selected rows (comma separated, default all rows of the
/// filters), the reader's <c>language</c> (default the tenant's) and the list's own filters and sort.
/// </summary>
public sealed record ExportRequest(string Source, string? Format, string? Columns, string? Ids, string? Language, IReadOnlyDictionary<string, string?> Parameters);

public sealed record ExportFile(string FileName, string ContentType, byte[] Content);

/// <summary>Either the file (small exports) or the queued export (large ones).</summary>
public sealed record ExportOutcome(ExportFile? File, ExportQueuedResponse? Queued);

public interface IExportManager
{
    /// <summary>
    /// Every row of the filters (or the selected ones), the visible columns with translated headers. Up to
    /// <see cref="ExportManager.MaxSyncRows"/> rows the file comes back at once, up to <see cref="ExportManager.MaxRows"/>
    /// it is queued for the Worker, beyond that the filters must be narrowed.
    /// </summary>
    Task<Result<ExportOutcome>> ExportAsync(ExportRequest request, CancellationToken cancellationToken);

    /// <summary>The Worker writes a queued export as the user who asked for it, then tells them.</summary>
    Task<Result> GenerateAsync(Guid exportId, CancellationToken cancellationToken);
}

public interface IExportQueryService
{
    /// <summary>The lists the caller may export, with their columns.</summary>
    Task<IReadOnlyList<ExportSourceResponse>> SourcesAsync(CancellationToken cancellationToken);

    /// <summary>The caller's queued exports of the last 24 hours.</summary>
    Task<Result<IReadOnlyList<ExportJobResponse>>> MineAsync(CancellationToken cancellationToken);

    /// <summary>The file of a ready export of the caller.</summary>
    Task<Result<ExportFile>> FileAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>What a queued export stores to be written later exactly as asked.</summary>
internal sealed record StoredExportRequest(
    string Source, string Format, IReadOnlyList<string> Columns, IReadOnlyList<Guid>? Ids, string Language, Dictionary<string, string?> Parameters);

internal sealed class ExportManager(
    IOperationRunner operations,
    IEnumerable<IExportSource> sources,
    IEnumerable<IExportWriter> writers,
    IExportJobDataFactory data,
    IMessageOutbox outbox,
    IAccessGuard guard,
    ILocalizer localizer,
    INotificationSender notifications,
    IRealtimeNotifier notifier,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock) : IExportManager
{
    public const int MaxSyncRows = 2000;
    public const int MaxRows = 50000;
    public const int PageSize = 100;
    public const int MaxIds = 1000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<ExportOutcome>> ExportAsync(ExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var prepared = await PrepareAsync(request, cancellationToken);
        if (prepared.IsFailure)
        {
            return Result.Failure<ExportOutcome>(prepared.Error!);
        }

        var (source, stored) = prepared.Value;
        var parameters = new ExportParameters(stored.Parameters);
        var first = await source.PageAsync(parameters, 1, 1, Formatting(stored.Language), cancellationToken);
        if (first.IsFailure)
        {
            return Result.Failure<ExportOutcome>(first.Error!);
        }

        var rows = stored.Ids is { } ids ? Math.Min(ids.Count, first.Value.Total) : first.Value.Total;
        if (first.Value.Total > MaxRows)
        {
            return Errors.Audit.TooLarge();
        }

        if (rows > MaxSyncRows)
        {
            return await QueueAsync(stored, rows, cancellationToken);
        }

        return await operations.RunAsync<ExportOutcome>(Operations.Audit.ExportList, new { request.Source, stored.Format }, async scope =>
        {
            var file = await BuildAsync(source, stored, cancellationToken);
            return file.IsFailure ? Result.Failure<ExportOutcome>(file.Error!) : new ExportOutcome(file.Value.File, null);
        }, cancellationToken);
    }

    public Task<Result> GenerateAsync(Guid exportId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Audit.GenerateExport, new { ExportId = exportId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(exportId, cancellationToken) is not { } job || job.UserId != currentUser.UserId)
            {
                return Errors.Audit.NotFound();
            }

            // Redelivered after it was written: nothing to do.
            if (job.Status != ExportJobStatus.Queued)
            {
                return Result.Success();
            }

            var stored = JsonSerializer.Deserialize<StoredExportRequest>(job.Request, Json)!;
            var now = clock.GetUtcNow();
            if (sources.FirstOrDefault(item => item.Key == stored.Source) is not { } source
                || (await guard.EnsureAsync(source.Permission, cancellationToken)).IsFailure)
            {
                job.Fail(Errors.Identity.PermissionDenied().DisplayCode, now);
                await store.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }

            var built = await BuildAsync(source, stored, cancellationToken);
            if (built.IsFailure)
            {
                job.Fail(built.Error!.DisplayCode, now);
                await store.SaveChangesAsync(cancellationToken);
                return Result.Success();
            }

            var (file, count) = built.Value;
            job.Complete(file.FileName, file.ContentType, file.Content, count, now);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("Export", job.Id);

            var userId = job.UserId;
            var ready = new ExportReadyEvent(job.Id, file.FileName);
            scope.OnCommitted(ct => notifier.ToUserAsync(userId, RealtimeEvents.ExportReady, ready, ct));
            await notifications.NotifyUsersAsync(
                [userId],
                new NotificationMessage(
                    NotificationKinds.ExportReady, job.Id, new Dictionary<string, string>(StringComparer.Ordinal) { ["file"] = file.FileName }, IncludeActor: true),
                cancellationToken);
            return Result.Success();
        }, cancellationToken);

    private async Task<Result<(IExportSource Source, StoredExportRequest Stored)>> PrepareAsync(ExportRequest request, CancellationToken cancellationToken)
    {
        if (sources.FirstOrDefault(item => string.Equals(item.Key, request.Source, StringComparison.Ordinal)) is not { } source)
        {
            return Errors.Audit.SourceNotFound();
        }

        var allowed = await guard.EnsureAsync(source.Permission, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<(IExportSource, StoredExportRequest)>(allowed.Error!);
        }

        var format = (request.Format ?? "csv").Trim().ToUpperInvariant();
        if (!Enum.TryParse<ExportFormat>(format, ignoreCase: true, out var parsedFormat) || !Enum.IsDefined(parsedFormat)
            || writers.All(writer => writer.Format != parsedFormat))
        {
            return Errors.Audit.Invalid("format", "validation.exports.format");
        }

        var known = source.Columns.Select(column => column.Id).ToHashSet(StringComparer.Ordinal);
        var columns = string.IsNullOrWhiteSpace(request.Columns)
            ? source.Columns.Select(column => column.Id).ToArray()
            : request.Columns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray();
        if (columns.Length == 0 || columns.Any(column => !known.Contains(column)))
        {
            return Errors.Audit.Invalid("columns", "validation.exports.columns");
        }

        List<Guid>? ids = null;
        if (!string.IsNullOrWhiteSpace(request.Ids))
        {
            ids = [];
            foreach (var part in request.Ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!Guid.TryParse(part, out var id) || ids.Count >= MaxIds)
                {
                    return Errors.Audit.Invalid("ids", "validation.exports.ids");
                }

                ids.Add(id);
            }
        }

        var parameters = request.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var reserved in new[] { "format", "columns", "ids", "language", "page", "pageSize" })
        {
            parameters.Remove(reserved);
        }

        var language = string.IsNullOrWhiteSpace(request.Language) ? tenant.Tenant.DefaultLanguage : request.Language.Trim();
        return (source, new StoredExportRequest(source.Key, parsedFormat.ToString(), columns, ids, language, parameters));
    }

    private async Task<Result<ExportOutcome>> QueueAsync(StoredExportRequest stored, int rows, CancellationToken cancellationToken) =>
        await operations.RunAsync<ExportOutcome>(Operations.Audit.QueueExport, new { stored.Source, stored.Format, Rows = rows }, async scope =>
        {
            if (currentUser.UserId is not { } userId)
            {
                return Errors.Identity.PermissionDenied();
            }

            var now = clock.GetUtcNow();
            await using var store = await data.OpenAsync(cancellationToken);
            await store.DeleteExpiredAsync(now, cancellationToken);
            var job = new ExportJob(Guid.CreateVersion7(), userId, stored.Source, stored.Format, JsonSerializer.Serialize(stored, Json), now);
            store.Add(job);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("Export", job.Id);
            await outbox.EnqueueAsync(scope, new GenerateExportCommand(job.Id, currentUser.Roles.Select(role => role.ToString()).ToArray()), cancellationToken);
            return new ExportOutcome(null, new ExportQueuedResponse(job.Id, rows));
        }, cancellationToken);

    /// <summary>Pages the list to the end (the selected rows only when ids are given) and writes the file.</summary>
    private async Task<Result<(ExportFile File, int Rows)>> BuildAsync(IExportSource source, StoredExportRequest stored, CancellationToken cancellationToken)
    {
        var writer = writers.First(item => item.Format == Enum.Parse<ExportFormat>(stored.Format));
        var formatting = Formatting(stored.Language);
        var parameters = new ExportParameters(stored.Parameters);
        var wanted = stored.Ids?.ToHashSet();
        var translated = source.Columns.Where(column => column.Translated).Select(column => column.Id).ToHashSet(StringComparer.Ordinal);
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var rows = new List<IReadOnlyList<string>>();
        for (var page = 1; ; page++)
        {
            var result = await source.PageAsync(parameters, page, PageSize, formatting, cancellationToken);
            if (result.IsFailure)
            {
                return Result.Failure<(ExportFile, int)>(result.Error!);
            }

            foreach (var row in result.Value.Rows.Where(row => wanted is null || wanted.Contains(row.Id)))
            {
                var values = new string[stored.Columns.Count];
                for (var index = 0; index < values.Length; index++)
                {
                    var value = row.Values.GetValueOrDefault(stored.Columns[index], string.Empty);
                    values[index] = translated.Contains(stored.Columns[index]) && value.Length > 0
                        ? await TranslateAsync(value, stored.Language, texts, cancellationToken)
                        : value;
                }

                rows.Add(values);
            }

            if (result.Value.Rows.Count < PageSize || page * PageSize >= Math.Min(result.Value.Total, MaxRows))
            {
                break;
            }
        }

        var language = stored.Language;
        var title = await localizer.GetAsync(source.TitleKey, language, null, cancellationToken);
        var labels = source.Columns.ToDictionary(column => column.Id, column => column.LabelKey, StringComparer.Ordinal);
        var headers = new List<string>();
        foreach (var column in stored.Columns)
        {
            headers.Add(await localizer.GetAsync(labels[column], language, null, cancellationToken));
        }

        var now = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), formatting.Zone);
        var appName = tenant.Tenant.Slug;
        var subtitle = await localizer.GetAsync(
            "exports.generated", language, new Dictionary<string, object?> { ["app"] = appName, ["date"] = now.DateTime.ToString("g", formatting.Culture) }, cancellationToken);
        var footer = await localizer.GetAsync("exports.total", language, new Dictionary<string, object?> { ["count"] = rows.Count }, cancellationToken);
        var table = new ExportTable(
            $"{appName} - {title}",
            subtitle,
            footer,
            headers,
            rows,
            await localizer.GetAsync("exports.page", language, null, cancellationToken),
            await localizer.GetAsync("exports.of", language, null, cancellationToken));
        var content = writer.Write(table);
        var fileName = $"{FileTitle(title)}_{now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture)}.{writer.Extension}";
        return (new ExportFile(fileName, writer.ContentType, content), rows.Count);
    }

    /// <summary>A translated value (statuses, types), each key looked up once per export.</summary>
    private async Task<string> TranslateAsync(string key, string language, Dictionary<string, string> texts, CancellationToken cancellationToken)
    {
        if (!texts.TryGetValue(key, out var text))
        {
            text = await localizer.GetAsync(key, language, null, cancellationToken);
            texts[key] = text;
        }

        return text;
    }

    private ExportFormatting Formatting(string language)
    {
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.InvariantCulture;
        }

        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(tenant.Tenant.TimeZone, out var found) ? found : TimeZoneInfo.Utc;
        return new ExportFormatting(zone, culture);
    }

    /// <summary>The title as a file name (legacy <c>{Title}_yyyyMMdd_HHmmss</c>): letters, digits, dashes.</summary>
    internal static string FileTitle(string title)
    {
        var clean = new string(title.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray()).Trim('_');
        while (clean.Contains("__", StringComparison.Ordinal))
        {
            clean = clean.Replace("__", "_", StringComparison.Ordinal);
        }

        return clean.Length == 0 ? "Export" : clean;
    }
}

internal sealed class ExportQueryService(IEnumerable<IExportSource> sources, IExportJobDataFactory data, IPermissionAccess permissions, ICurrentUser currentUser, TimeProvider clock)
    : IExportQueryService
{
    public async Task<IReadOnlyList<ExportSourceResponse>> SourcesAsync(CancellationToken cancellationToken)
    {
        var granted = await permissions.GetGrantedAsync(cancellationToken);
        return sources
            .Where(source => granted.Contains(source.Permission))
            .OrderBy(source => source.Key, StringComparer.Ordinal)
            .Select(source => new ExportSourceResponse(source.Key, source.TitleKey, source.Columns.Select(column => new ExportColumnResponse(column.Id, column.LabelKey)).ToArray()))
            .ToArray();
    }

    public async Task<Result<IReadOnlyList<ExportJobResponse>>> MineAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Errors.Identity.PermissionDenied();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.OfUserAsync(userId, clock.GetUtcNow(), cancellationToken))
            .Select(job => new ExportJobResponse(job.Id, job.SourceKey, job.Format, job.Status.ToString(), job.FileName, job.RowCount, job.ErrorCode, job.CreatedAt, job.ExpiresAt))
            .ToArray();
    }

    public async Task<Result<ExportFile>> FileAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindAsync(id, cancellationToken) is not { } job || job.UserId != currentUser.UserId || job.ExpiresAt <= clock.GetUtcNow())
        {
            return Errors.Audit.NotFound();
        }

        return job.Status == ExportJobStatus.Ready && job.Content is { } content
            ? new ExportFile(job.FileName!, job.ContentType!, content)
            : Errors.Audit.NotReady();
    }
}
