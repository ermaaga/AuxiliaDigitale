namespace Auxilia.Contracts.Platform;

/// <summary>
/// Log level of a tenant (D-28): <c>defaultLevel</c> applies to every tenant; <c>debugUntil</c> is set while the
/// tenant's Debug events are written and ends by itself.
/// </summary>
public sealed record TenantLogLevelResponse(string DefaultLevel, DateTimeOffset? DebugUntil);

/// <summary>Writes the tenant's Debug events until <c>until</c> (within the next 24 hours).</summary>
public sealed record EnableTenantDebugLoggingRequest(DateTimeOffset Until);

/// <summary>
/// Search of a tenant's daily log files (F25, D-17), newest first. <c>from</c>/<c>to</c> are UTC days (at most 31);
/// <c>level</c> is the minimum level; <c>text</c> searches message and exception; <c>cursor</c> continues a previous page.
/// </summary>
public sealed record TenantLogQuery(
    DateOnly? From,
    DateOnly? To,
    string? Level,
    string? Code,
    string? TraceId,
    string? UserId,
    string? Text,
    string? Cursor,
    int? PageSize);

/// <summary>
/// One log event. <c>level</c> is Verbose/Debug/Information/Warning/Error/Fatal; <c>eventCode</c> is <c>AUX-NNNNN</c>
/// for Auxilia events; <c>properties</c> holds the remaining fields of the line (already masked when written).
/// </summary>
public sealed record TenantLogEntryResponse(
    DateTimeOffset Timestamp,
    string Level,
    string? EventCode,
    string Message,
    string? Exception,
    string? TraceId,
    string? UserId,
    string? Operation,
    string? Source,
    IReadOnlyDictionary<string, string> Properties);

/// <summary>A page of log events; <c>nextCursor</c> is set when older events may follow.</summary>
public sealed record TenantLogPageResponse(IReadOnlyList<TenantLogEntryResponse> Items, string? NextCursor);
