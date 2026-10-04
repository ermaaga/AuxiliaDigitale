namespace Auxilia.Contracts.Reporting;

/// <summary>
/// A large export queued for the Worker (202, F26): the file is announced by the notification <c>export.ready</c> and
/// the realtime <c>ExportReady</c>, then downloaded from <c>/api/v1/exports/{id}/file</c>.
/// </summary>
public sealed record ExportQueuedResponse(Guid Id, int RowCount);

/// <summary>An export of the caller kept for 24 hours: <c>status</c> <c>Queued</c>, <c>Ready</c> or <c>Failed</c>.</summary>
public sealed record ExportJobResponse(
    Guid Id, string Source, string Format, string Status, string? FileName, int? RowCount, string? ErrorCode, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

/// <summary>A list that can be exported, with its columns (id and header translation key).</summary>
public sealed record ExportSourceResponse(string Key, string TitleKey, IReadOnlyList<ExportColumnResponse> Columns);

public sealed record ExportColumnResponse(string Id, string LabelKey);
