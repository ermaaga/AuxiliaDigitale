using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Reporting;

public enum ExportJobStatus
{
    Queued,
    Ready,
    Failed,
}

/// <summary>
/// A large export run by the Worker (<c>reporting.exports</c>, F26): the request (list, format, columns, filters,
/// language), then the file kept for <see cref="RetentionHours"/> hours for the user who asked for it.
/// </summary>
public sealed class ExportJob : AggregateRoot<Guid>
{
    public const int RetentionHours = 24;
    public const int FileNameMaxLength = 200;

    public ExportJob(Guid id, Guid userId, string sourceKey, string format, string request, DateTimeOffset now)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        UserId = userId;
        SourceKey = sourceKey;
        Format = format;
        Request = request;
        Status = ExportJobStatus.Queued;
        CreatedAt = now;
        ExpiresAt = now.AddHours(RetentionHours);
    }

    private ExportJob()
    {
        SourceKey = Format = Request = string.Empty;
    }

    public Guid UserId { get; private set; }

    /// <summary>The list (<c>IExportSource.Key</c>).</summary>
    public string SourceKey { get; private set; }

    public string Format { get; private set; }

    /// <summary>Columns, filters, selected ids and language (JSON).</summary>
    public string Request { get; private set; }

    public ExportJobStatus Status { get; private set; }

    public string? FileName { get; private set; }

    public string? ContentType { get; private set; }

    public byte[]? Content { get; private set; }

    public int? RowCount { get; private set; }

    public string? ErrorCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public void Complete(string fileName, string contentType, byte[] content, int rowCount, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        FileName = fileName.Length > FileNameMaxLength ? fileName[..FileNameMaxLength] : fileName;
        ContentType = contentType;
        Content = content;
        RowCount = rowCount;
        Status = ExportJobStatus.Ready;
        CompletedAt = now;
        ExpiresAt = now.AddHours(RetentionHours);
    }

    public void Fail(string errorCode, DateTimeOffset now)
    {
        ErrorCode = errorCode;
        Status = ExportJobStatus.Failed;
        CompletedAt = now;
    }
}
