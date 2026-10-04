namespace Auxilia.Contracts.Imports;

/// <summary>A column of an import: the header in the template is <c>key</c>; <c>labelKey</c> is a translation key.</summary>
public sealed record ImportFieldResponse(string Key, string LabelKey, bool Required);

/// <summary>An entity that can be imported (F19) with its columns.</summary>
public sealed record ImportEntityResponse(string Entity, IReadOnlyList<ImportFieldResponse> Fields);

public sealed record ImportTypeResponse(Guid Id, string Name, string TargetEntity, DateTimeOffset CreatedAt, int ImportCount);

/// <summary>A named import type for an entity; the name defaults to the entity.</summary>
public sealed record CreateImportTypeRequest(string? Name, string TargetEntity);

public sealed record CreateImportTypeResponse(Guid Id);

/// <param name="Status"><c>Pending</c>, <c>Validating</c>, <c>AwaitingConfirmation</c>, <c>Processing</c>, <c>Completed</c>, <c>Failed</c> or <c>Cancelled</c>.</param>
/// <param name="Progress">0–100: rows validated (Validating) or handled (Processing) over the total.</param>
public sealed record ImportJobResponse(
    Guid Id,
    Guid ImportTypeId,
    string ImportTypeName,
    string TargetEntity,
    string Name,
    string FileName,
    string Status,
    int Progress,
    int TotalRows,
    int ProcessedRows,
    int SuccessRows,
    int FailedRows,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

/// <summary>A row of an import: the values read, the errors per field (translation keys) and the record created.</summary>
/// <param name="Status"><c>Valid</c>, <c>Invalid</c>, <c>Imported</c> or <c>Failed</c>.</param>
public sealed record ImportRowResponse(
    int RowNumber, string Status, IReadOnlyDictionary<string, string> Values, IReadOnlyDictionary<string, string[]>? Errors, Guid? EntityId);

public sealed record StartImportResponse(Guid Id);
