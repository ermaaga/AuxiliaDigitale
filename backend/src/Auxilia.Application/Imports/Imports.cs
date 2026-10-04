using System.Text.Json;

using Auxilia.Application.Abstractions.Imports;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Imports;
using Auxilia.Contracts.Messages.V1.Imports;
using Auxilia.Diagnostics;
using Auxilia.Domain.Imports;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Imports;

/// <summary>Limits of an import (F19).</summary>
public static class ImportLimits
{
    /// <summary>Legacy limit: 10 MB.</summary>
    public const int MaxFileBytes = 10 * 1024 * 1024;

    public const int MaxRows = 5000;
}

/// <summary>The import types of the tenant (F19, D-18: the System from the console).</summary>
public interface IImportTypeManager
{
    /// <summary>A named type for a registered entity; the name defaults to the entity and is unique.</summary>
    Task<Result<Guid>> CreateAsync(CreateImportTypeRequest request, CancellationToken cancellationToken);

    /// <summary>Only a type without imports.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

/// <param name="Content">The uploaded workbook.</param>
public sealed record StartImportRequest(string? Name, Guid ImportTypeId, string? FileName, byte[] Content);

/// <summary>
/// Imports (F19, Q47/Q48): upload → the Worker validates every row → preview → confirm (the Worker imports the valid
/// rows, each through the Manager of its module) or cancel. Only finished imports can be deleted.
/// </summary>
public interface IImportManager
{
    /// <summary>An Excel workbook (<c>.xlsx</c>, at most <see cref="ImportLimits.MaxFileBytes"/>): queued for validation.</summary>
    Task<Result<Guid>> StartAsync(StartImportRequest request, CancellationToken cancellationToken);

    /// <summary>Worker: reads the file and validates the rows; idempotent (only a pending import starts).</summary>
    Task<Result> ValidateAsync(Guid id, CancellationToken cancellationToken);

    Task<Result> ConfirmAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Worker: imports the valid rows still to do; idempotent (only a processing import continues).</summary>
    Task<Result> ProcessAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>An import waiting for confirmation: its rows are discarded.</summary>
    Task<Result> CancelAsync(Guid id, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record ImportTemplate(string FileName, byte[] Content);

public interface IImportQueryService
{
    IReadOnlyList<ImportEntityResponse> Entities();

    Task<IReadOnlyList<ImportTypeResponse>> TypesAsync(CancellationToken cancellationToken);

    /// <summary>The Excel template of a type: one header row, required columns red.</summary>
    Task<Result<ImportTemplate>> TemplateAsync(Guid typeId, CancellationToken cancellationToken);

    Task<PagedResponse<ImportJobResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<Result<ImportJobResponse>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <param name="status"><c>Valid</c>, <c>Invalid</c>, <c>Imported</c>, <c>Failed</c> or none.</param>
    Task<Result<PagedResponse<ImportRowResponse>>> RowsAsync(Guid id, string? status, int page, int pageSize, CancellationToken cancellationToken);
}

internal sealed class ImportTypeManager(IOperationRunner operations, IImportDataFactory data, IEnumerable<IImportTarget> targets, TimeProvider clock)
    : IImportTypeManager
{
    public Task<Result<Guid>> CreateAsync(CreateImportTypeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Imports.CreateImportType, new { request.TargetEntity }, async scope =>
        {
            var target = targets.FirstOrDefault(item => string.Equals(item.Entity, request.TargetEntity, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                return Errors.Imports.ImportTypeInvalid("targetEntity", "validation.imports.entity");
            }

            var name = string.IsNullOrWhiteSpace(request.Name) ? target.Entity : request.Name.Trim();
            if (name.Length > ImportType.NameMaxLength)
            {
                return Errors.Imports.ImportTypeInvalid("name", "validation.imports.typeName");
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if ((await store.TypesAsync(cancellationToken)).Any(type => string.Equals(type.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return Errors.Imports.ImportTypeInvalid("name", "validation.imports.typeNameTaken");
            }

            var type = new ImportType(Guid.CreateVersion7(), name, target.Entity, clock.GetUtcNow());
            store.Add(type);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity(nameof(ImportType), type.Id);
            return type.Id;
        }, cancellationToken);
    }

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Imports.DeleteImportType, new { ImportTypeId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindTypeAsync(id, cancellationToken) is not { } type)
            {
                return Errors.Imports.ImportTypeNotFound();
            }

            if (await store.TypeHasJobsAsync(id, cancellationToken))
            {
                return Errors.Imports.ImportTypeInUse();
            }

            store.Remove(type);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
}

internal sealed class ImportManager(
    IOperationRunner operations,
    IImportDataFactory data,
    IImportWorkbook workbook,
    IEnumerable<IImportTarget> targets,
    IMessageOutbox outbox,
    TimeProvider clock) : IImportManager
{
    /// <summary>Rows saved (validation) or imported (processing) per batch: the progress the console sees.</summary>
    public const int BatchSize = 50;

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<Result<Guid>> StartAsync(StartImportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<Guid>(Operations.Imports.StartImport, new { request.ImportTypeId }, async scope =>
        {
            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var name = request.Name?.Trim() ?? string.Empty;
            if (name.Length is 0 or > ImportJob.NameMaxLength)
            {
                errors["name"] = ["validation.imports.name"];
            }

            // Only the name: browsers on Windows may send the whole path.
            var fileName = (request.FileName ?? string.Empty).Split('/', '\\')[^1].Trim();
            if (!fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || request.Content.Length == 0)
            {
                errors["file"] = ["validation.imports.file"];
            }
            else if (request.Content.Length > ImportLimits.MaxFileBytes)
            {
                errors["file"] = ["validation.imports.fileSize"];
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindTypeAsync(request.ImportTypeId, cancellationToken) is null)
            {
                errors["importTypeId"] = ["validation.imports.type"];
            }

            if (errors.Count > 0)
            {
                return Errors.Imports.ImportInvalid(errors);
            }

            var job = new ImportJob(Guid.CreateVersion7(), request.ImportTypeId, name, fileName, request.Content, clock.GetUtcNow());
            store.Add(job);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("Import", job.Id);
            await outbox.EnqueueAsync(scope, new ValidateImportCommand(job.Id), cancellationToken);
            return job.Id;
        }, cancellationToken);
    }

    public Task<Result> ValidateAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Imports.ValidateImport, new { ImportId = id }, async _ =>
        {
            byte[] content;
            IImportTarget? target;
            await using (var store = await data.OpenAsync(cancellationToken))
            {
                if (await store.FindJobAsync(id, cancellationToken) is not { } job)
                {
                    return Errors.Imports.ImportNotFound();
                }

                if (job.Status is not (ImportJobStatus.Pending or ImportJobStatus.Validating) || job.FileContent is null)
                {
                    return Result.Success();
                }

                content = job.FileContent;
                var type = await store.FindTypeAsync(job.ImportTypeId, cancellationToken);
                target = type is null ? null : Target(type.TargetEntity);
            }

            if (target is null)
            {
                return await FailAsync(id, "the import type has no target", cancellationToken);
            }

            var sheet = workbook.Read(content, target.Fields);
            if (sheet is null)
            {
                return await FailAsync(id, "not an Excel workbook", cancellationToken);
            }

            if (sheet.MissingColumns.Count > 0)
            {
                return await FailAsync(id, "missing columns " + string.Join(", ", sheet.MissingColumns), cancellationToken);
            }

            if (sheet.Rows.Count == 0)
            {
                return await FailAsync(id, "no rows", cancellationToken);
            }

            if (sheet.Rows.Count > ImportLimits.MaxRows)
            {
                return await FailAsync(id, $"more than {ImportLimits.MaxRows} rows", cancellationToken);
            }

            var started = await ChangeAsync(id, job => job.StartValidation(sheet.Rows.Count, clock.GetUtcNow()) ? null : job, cancellationToken);
            if (started is not null)
            {
                return Result.Success();
            }

            // A redelivery after a Worker restart validates again from the start.
            await operations.RunAsync(Operations.Imports.SaveImportProgress, new { ImportId = id }, async _ =>
            {
                await using var store = await data.OpenAsync(cancellationToken);
                await store.DeleteRowsAsync(id, cancellationToken);
                return Result.Success();
            }, cancellationToken);

            var checks = await target.ValidateAsync(sheet.Rows, cancellationToken);
            foreach (var batch in sheet.Rows.Select((row, index) => (row, check: checks[index])).Chunk(BatchSize))
            {
                await operations.RunAsync(Operations.Imports.SaveImportProgress, new { ImportId = id }, async _ =>
                {
                    await using var store = await data.OpenAsync(cancellationToken);
                    var job = await store.FindJobAsync(id, cancellationToken);
                    store.AddRows(batch.Select(item => new ImportJobRow(
                        id,
                        item.row.RowNumber,
                        JsonSerializer.Serialize(item.row.Values, Json),
                        item.check.IsValid ? null : JsonSerializer.Serialize(item.check.Errors, Json))));
                    job!.RecordValidated(batch.Count(item => item.check.IsValid), batch.Count(item => !item.check.IsValid));
                    await store.SaveChangesAsync(cancellationToken);
                    return Result.Success();
                }, cancellationToken);
            }

            await ChangeAsync(id, job =>
            {
                job.AwaitConfirmation();
                return null;
            }, cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> ConfirmAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Imports.ConfirmImport, new { ImportId = id }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindJobAsync(id, cancellationToken) is not { } job)
            {
                return Errors.Imports.ImportNotFound();
            }

            if (!job.Confirm())
            {
                return Errors.Imports.ImportStatusInvalid(job.Status.ToString());
            }

            await store.SaveChangesAsync(cancellationToken);
            await outbox.EnqueueAsync(scope, new ProcessImportCommand(job.Id), cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> ProcessAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Imports.ProcessImport, new { ImportId = id }, async _ =>
        {
            IImportTarget? target;
            await using (var store = await data.OpenAsync(cancellationToken))
            {
                if (await store.FindJobAsync(id, cancellationToken) is not { } job)
                {
                    return Errors.Imports.ImportNotFound();
                }

                if (job.Status != ImportJobStatus.Processing)
                {
                    return Result.Success();
                }

                var type = await store.FindTypeAsync(job.ImportTypeId, cancellationToken);
                target = type is null ? null : Target(type.TargetEntity);
            }

            if (target is null)
            {
                return await FailAsync(id, "the import type has no target", cancellationToken);
            }

            var afterRow = 0;
            while (true)
            {
                IReadOnlyList<ImportJobRow> batch;
                await using (var store = await data.OpenAsync(cancellationToken))
                {
                    batch = await store.ValidRowsAsync(id, afterRow, BatchSize, cancellationToken);
                }

                if (batch.Count == 0)
                {
                    break;
                }

                // Every row on its own: a failing row does not undo the others.
                var outcomes = new List<(int RowNumber, Result<Guid> Result)>();
                foreach (var row in batch)
                {
                    var values = JsonSerializer.Deserialize<Dictionary<string, string>>(row.Data, Json) ?? [];
                    outcomes.Add((row.RowNumber, await target.ImportAsync(new ImportRow(row.RowNumber, values), cancellationToken)));
                }

                await operations.RunAsync(Operations.Imports.SaveImportProgress, new { ImportId = id }, async _ =>
                {
                    await using var store = await data.OpenAsync(cancellationToken);
                    var job = await store.FindJobAsync(id, cancellationToken);
                    var rows = (await store.ValidRowsAsync(id, afterRow, BatchSize, cancellationToken)).ToDictionary(row => row.RowNumber);
                    foreach (var (rowNumber, result) in outcomes)
                    {
                        if (!rows.TryGetValue(rowNumber, out var row))
                        {
                            continue;
                        }

                        if (result.IsSuccess)
                        {
                            row.Imported(result.Value);
                        }
                        else
                        {
                            var errors = new ImportRowErrors();
                            errors.Add(result.Error!);
                            row.Failed(JsonSerializer.Serialize(errors.Errors, Json));
                        }
                    }

                    job!.RecordProcessed(outcomes.Count(item => item.Result.IsSuccess), outcomes.Count(item => item.Result.IsFailure));
                    await store.SaveChangesAsync(cancellationToken);
                    return Result.Success();
                }, cancellationToken);
                afterRow = batch[^1].RowNumber;
            }

            await ChangeAsync(id, job =>
            {
                job.Complete(clock.GetUtcNow());
                return null;
            }, cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> CancelAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Imports.CancelImport, new { ImportId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindJobAsync(id, cancellationToken) is not { } job)
            {
                return Errors.Imports.ImportNotFound();
            }

            if (!job.Cancel(clock.GetUtcNow()))
            {
                return Errors.Imports.ImportStatusInvalid(job.Status.ToString());
            }

            await store.SaveChangesAsync(cancellationToken);
            await store.DeleteRowsAsync(id, cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Imports.DeleteImport, new { ImportId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindJobAsync(id, cancellationToken) is not { } job)
            {
                return Errors.Imports.ImportNotFound();
            }

            if (!job.IsFinished)
            {
                return Errors.Imports.ImportStatusInvalid(job.Status.ToString());
            }

            store.Remove(job);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    private IImportTarget? Target(string entity) =>
        targets.FirstOrDefault(target => string.Equals(target.Entity, entity, StringComparison.OrdinalIgnoreCase));

    /// <summary>Applies a change to the job in a write operation of its own; returns what <paramref name="change"/> returns.</summary>
    private async Task<ImportJob?> ChangeAsync(Guid id, Func<ImportJob, ImportJob?> change, CancellationToken cancellationToken)
    {
        ImportJob? outcome = null;
        await operations.RunAsync(Operations.Imports.SaveImportProgress, new { ImportId = id }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var job = await store.FindJobAsync(id, cancellationToken);
            outcome = change(job!);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
        return outcome;
    }

    /// <summary>The file cannot be used: the import fails with <c>AUX-22010</c> (the validation itself succeeded).</summary>
    private async Task<Result> FailAsync(Guid id, string reason, CancellationToken cancellationToken)
    {
        var error = Errors.Imports.ImportFileUnreadable(reason);
        await ChangeAsync(id, job =>
        {
            job.Fail(error.DisplayCode, error.Description, clock.GetUtcNow());
            return null;
        }, cancellationToken);
        return Result.Success();
    }
}

internal sealed class ImportQueryService(IImportDataFactory data, IImportWorkbook workbook, IEnumerable<IImportTarget> targets) : IImportQueryService
{
    public const int MaxPageSize = 100;

    public IReadOnlyList<ImportEntityResponse> Entities() =>
        [.. targets.OrderBy(target => target.Entity, StringComparer.Ordinal)
            .Select(target => new ImportEntityResponse(target.Entity, [.. target.Fields.Select(field => new ImportFieldResponse(field.Key, field.LabelKey, field.Required))]))];

    public async Task<IReadOnlyList<ImportTypeResponse>> TypesAsync(CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return [.. (await store.TypesAsync(cancellationToken)).Select(type => new ImportTypeResponse(type.Id, type.Name, type.TargetEntity, type.CreatedAt, type.ImportCount))];
    }

    public async Task<Result<ImportTemplate>> TemplateAsync(Guid typeId, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindTypeAsync(typeId, cancellationToken) is not { } type
            || targets.FirstOrDefault(target => string.Equals(target.Entity, type.TargetEntity, StringComparison.OrdinalIgnoreCase)) is not { } target)
        {
            return Errors.Imports.ImportTypeNotFound();
        }

        var name = new string([.. type.Name.Select(character => char.IsLetterOrDigit(character) ? character : '_')]);
        return new ImportTemplate($"{name}_template.xlsx", workbook.Template(type.Name, target.Fields));
    }

    public async Task<PagedResponse<ImportJobResponse>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        (page, pageSize) = (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
        await using var store = await data.OpenAsync(cancellationToken);
        var (items, total) = await store.JobsAsync(page, pageSize, cancellationToken);
        return new PagedResponse<ImportJobResponse>([.. items.Select(ToResponse)], page, pageSize, total);
    }

    public async Task<Result<ImportJobResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return await store.JobAsync(id, cancellationToken) is { } job ? ToResponse(job) : Errors.Imports.ImportNotFound();
    }

    public async Task<Result<PagedResponse<ImportRowResponse>>> RowsAsync(Guid id, string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        ImportRowStatus? wanted = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ImportRowStatus>(status, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Errors.Imports.ImportInvalid("status", "validation.imports.rowStatus");
            }

            wanted = parsed;
        }

        (page, pageSize) = (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.JobAsync(id, cancellationToken) is null)
        {
            return Errors.Imports.ImportNotFound();
        }

        var (items, total) = await store.RowsAsync(id, wanted, page, pageSize, cancellationToken);
        return new PagedResponse<ImportRowResponse>(
            [.. items.Select(row => new ImportRowResponse(
                row.RowNumber,
                row.Status.ToString(),
                JsonSerializer.Deserialize<Dictionary<string, string>>(row.Data, ImportManager.Json) ?? [],
                row.Errors is null ? null : JsonSerializer.Deserialize<Dictionary<string, string[]>>(row.Errors, ImportManager.Json),
                row.EntityId))],
            page,
            pageSize,
            total);
    }

    private static ImportJobResponse ToResponse(ImportJobListRow job)
    {
        var done = job.Status switch
        {
            ImportJobStatus.Validating => job.ProcessedRows,
            ImportJobStatus.Processing => job.ProcessedRows,
            ImportJobStatus.Pending => 0,
            ImportJobStatus.AwaitingConfirmation or ImportJobStatus.Completed or ImportJobStatus.Cancelled => job.TotalRows,
            _ => job.ProcessedRows,
        };
        var progress = job.TotalRows == 0 ? (job.Status is ImportJobStatus.Completed ? 100 : 0) : Math.Min(100, done * 100 / job.TotalRows);
        return new ImportJobResponse(
            job.Id, job.ImportTypeId, job.ImportTypeName, job.TargetEntity, job.Name, job.FileName, job.Status.ToString(), progress, job.TotalRows,
            job.ProcessedRows, job.SuccessRows, job.FailedRows, job.ErrorCode, job.ErrorMessage, job.CreatedAt, job.StartedAt, job.CompletedAt);
    }
}
