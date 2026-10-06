using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Imports;

/// <summary>A named import type of the tenant for one target entity (<c>imports.import_types</c>, F19, legacy <c>ImportType</c>).</summary>
public sealed class ImportType : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 100;
    public const int EntityMaxLength = 50;

    public ImportType(Guid id, string name, string targetEntity, DateTimeOffset now)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetEntity);
        Name = name.Trim();
        TargetEntity = targetEntity;
        CreatedAt = now;
    }

    private ImportType()
    {
        Name = TargetEntity = string.Empty;
    }

    public string Name { get; private set; }

    /// <summary>The target (<c>IImportTarget.Entity</c>): <c>Employee</c>, <c>Client</c>, <c>Service</c>, <c>Case</c>.</summary>
    public string TargetEntity { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

public enum ImportJobStatus
{
    /// <summary>Uploaded, the Worker has not started yet.</summary>
    Pending,

    Validating,

    /// <summary>Rows validated: the System looks at the preview and confirms or cancels.</summary>
    AwaitingConfirmation,

    Processing,

    Completed,

    /// <summary>The file could not be read (or the Worker failed): nothing was imported.</summary>
    Failed,

    Cancelled,
}

/// <summary>
/// One import of a file (<c>imports.import_jobs</c>, F19, Q48: the legacy <c>Import</c> and <c>ImportJob</c> merged):
/// Pending → Validating → AwaitingConfirmation → Processing → Completed, or Failed, or Cancelled before processing.
/// The uploaded workbook is kept only until it is validated.
/// </summary>
public sealed class ImportJob : AggregateRoot<Guid>
{
    public const int NameMaxLength = 100;
    public const int FileNameMaxLength = 255;

    public ImportJob(Guid id, Guid importTypeId, string name, string fileName, byte[] content, DateTimeOffset now)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        ImportTypeId = importTypeId;
        Name = name.Trim();
        FileName = fileName.Length > FileNameMaxLength ? fileName[..FileNameMaxLength] : fileName;
        FileContent = content;
        Status = ImportJobStatus.Pending;
        CreatedAt = now;
    }

    private ImportJob()
    {
        Name = FileName = string.Empty;
    }

    public Guid ImportTypeId { get; private set; }

    public string Name { get; private set; }

    public string FileName { get; private set; }

    /// <summary>The workbook until validation, then <c>null</c>.</summary>
    public byte[]? FileContent { get; private set; }

    public ImportJobStatus Status { get; private set; }

    public int TotalRows { get; private set; }

    /// <summary>Rows validated (while validating) or handled (while processing).</summary>
    public int ProcessedRows { get; private set; }

    /// <summary>Valid rows after validation, imported rows after processing.</summary>
    public int SuccessRows { get; private set; }

    /// <summary>Invalid rows after validation, plus rows that failed while importing.</summary>
    public int FailedRows { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Finished imports can be deleted.</summary>
    public bool IsFinished => Status is ImportJobStatus.Completed or ImportJobStatus.Failed or ImportJobStatus.Cancelled;

    /// <summary>Starts (or, after a Worker restart, starts again) the validation.</summary>
    /// <returns>False when it is past validation (a redelivered message).</returns>
    /// <summary>Legacy import (E-05): a finished legacy import, kept as history (no file, no rows).</summary>
    public static ImportJob ImportLegacy(
        Guid id, Guid importTypeId, string name, string fileName, ImportJobStatus status, (int Total, int Processed, int Success, int Failed) rows,
        string? errorMessage, DateTimeOffset createdAt, DateTimeOffset? startedAt, DateTimeOffset? completedAt) =>
        new(id, importTypeId, string.IsNullOrWhiteSpace(name) ? fileName : name, fileName, [], createdAt)
        {
            FileContent = null,
            Status = status,
            TotalRows = rows.Total,
            ProcessedRows = rows.Processed,
            SuccessRows = rows.Success,
            FailedRows = rows.Failed,
            ErrorMessage = errorMessage,
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };

    public bool StartValidation(int totalRows, DateTimeOffset now)
    {
        if (Status is not (ImportJobStatus.Pending or ImportJobStatus.Validating) || FileContent is null)
        {
            return false;
        }

        Status = ImportJobStatus.Validating;
        TotalRows = totalRows;
        ProcessedRows = SuccessRows = FailedRows = 0;
        StartedAt = now;
        return true;
    }

    public void RecordValidated(int valid, int invalid)
    {
        SuccessRows += valid;
        FailedRows += invalid;
        ProcessedRows = SuccessRows + FailedRows;
    }

    public void AwaitConfirmation()
    {
        Status = ImportJobStatus.AwaitingConfirmation;
        FileContent = null;
    }

    public bool Confirm()
    {
        if (Status != ImportJobStatus.AwaitingConfirmation)
        {
            return false;
        }

        // The invalid rows are already handled: they are skipped.
        Status = ImportJobStatus.Processing;
        ProcessedRows = FailedRows;
        SuccessRows = 0;
        return true;
    }

    /// <param name="failedRows">Rows that failed while importing (added to the invalid ones).</param>
    public void RecordProcessed(int imported, int failedRows)
    {
        SuccessRows += imported;
        FailedRows += failedRows;
        ProcessedRows += imported + failedRows;
    }

    public void Complete(DateTimeOffset now)
    {
        Status = ImportJobStatus.Completed;
        CompletedAt = now;
    }

    public void Fail(string errorCode, string message, DateTimeOffset now)
    {
        Status = ImportJobStatus.Failed;
        ErrorCode = errorCode;
        ErrorMessage = message.Length > 500 ? message[..500] : message;
        FileContent = null;
        CompletedAt = now;
    }

    public bool Cancel(DateTimeOffset now)
    {
        if (Status != ImportJobStatus.AwaitingConfirmation)
        {
            return false;
        }

        Status = ImportJobStatus.Cancelled;
        CompletedAt = now;
        return true;
    }
}

public enum ImportRowStatus
{
    Valid,
    Invalid,
    Imported,
    Failed,
}

/// <summary>A row of an import (<c>imports.import_job_rows</c>): the values read, the errors per field and the record created.</summary>
public sealed class ImportJobRow
{
    public ImportJobRow(Guid jobId, int rowNumber, string data, string? errors)
    {
        JobId = jobId;
        RowNumber = rowNumber;
        Data = data;
        Errors = errors;
        Status = errors is null ? ImportRowStatus.Valid : ImportRowStatus.Invalid;
    }

    private ImportJobRow()
    {
        Data = string.Empty;
    }

    public Guid JobId { get; private set; }

    /// <summary>The row of the worksheet (2 = first data row).</summary>
    public int RowNumber { get; private set; }

    public ImportRowStatus Status { get; private set; }

    /// <summary>Column key → value as text (JSON).</summary>
    public string Data { get; private set; }

    /// <summary>Field → translation keys (JSON); <c>null</c> when valid.</summary>
    public string? Errors { get; private set; }

    public Guid? EntityId { get; private set; }

    public void Imported(Guid entityId)
    {
        Status = ImportRowStatus.Imported;
        EntityId = entityId;
    }

    public void Failed(string errors)
    {
        Status = ImportRowStatus.Failed;
        Errors = errors;
    }
}
