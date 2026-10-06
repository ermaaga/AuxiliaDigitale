using Auxilia.Domain.Imports;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-05 (F19): <c>ImportTypes</c> → <c>imports.import_types</c>, <c>Imports</c> and <c>ImportJobs</c> (when the table
/// exists, Q61) → <c>imports.import_jobs</c> as history only: no file, no rows, unfinished runs cancelled.
/// </summary>
internal sealed class ImportHistoryStep : ILegacyImportStep
{
    public const string TypesTable = "ImportTypes";
    public const string ImportsTable = "Imports";
    public const string JobsTable = "ImportJobs";

    public string Name => "import history";

    /// <summary>Legacy target entity → the entity of an <c>IImportTarget</c> (S-08).</summary>
    public static string TargetEntity(string legacy) => legacy switch
    {
        "Subscription" => "Case",
        "Membership" => "Service",
        _ => legacy,
    };

    /// <summary>Legacy run status → a finished status (a run the legacy never finished is cancelled).</summary>
    public static ImportJobStatus Status(string legacy) => legacy switch
    {
        "Completed" or "Concluded" => ImportJobStatus.Completed,
        "Failed" => ImportJobStatus.Failed,
        _ => ImportJobStatus.Cancelled,
    };

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var types = context.Report.For(TypesTable);
        var names = (await context.Tenant.Set<ImportType>().Select(type => type.Name).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var legacy in await context.Legacy.ImportTypes.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            if (context.Ids.Find(TypesTable, legacy.Id) is not null)
            {
                continue;
            }

            var fitted = LegacyText.Fit(legacy.Name, ImportType.NameMaxLength, out _);
            var name = ServiceCatalogStep.UniqueName(fitted.Length == 0 ? legacy.TargetEntity : fitted, legacy.Id, ImportType.NameMaxLength, names, out var renamed);
            if (renamed)
            {
                context.Report.Warn(TypesTable, legacy.Id, "name already used by another import type: legacy id appended");
            }

            names.Add(name);
            var type = new ImportType(
                context.Ids.NewId(), name, LegacyText.Fit(TargetEntity(legacy.TargetEntity), ImportType.EntityMaxLength, out _),
                LegacyImportContext.Instant(legacy.CreatedAt));
            context.Tenant.Add(type);
            context.Ids.Add(TypesTable, legacy.Id, type.Id);
            types.Created++;
        }

        var imports = context.Report.For(ImportsTable);
        foreach (var legacy in await context.Legacy.Imports.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            Add(context, imports, ImportsTable, legacy.Id, legacy.ImportTypeId, legacy.Name, legacy.FileName, legacy.Status, (0, 0, 0, 0), legacy.ErrorMessage,
                legacy.CreatedAt, null, legacy.CompletedAt);
        }

        if (!context.Legacy.Variant.ImportJobs)
        {
            return;
        }

        var jobs = context.Report.For(JobsTable);
        foreach (var legacy in await context.Legacy.ImportJobs.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            Add(context, jobs, JobsTable, legacy.Id, legacy.ImportTypeId, legacy.FileName, legacy.FileName, legacy.Status,
                (legacy.TotalRows, legacy.ProcessedRows, legacy.SuccessRows, legacy.FailedRows), legacy.ErrorMessage, legacy.CreatedAt, legacy.StartedAt, legacy.CompletedAt);
        }
    }

    private static void Add(
        LegacyImportContext context, LegacyTableResult result, string table, int legacyId, int legacyTypeId, string name, string fileName, string status,
        (int, int, int, int) rows, string? error, DateTime createdAt, DateTime? startedAt, DateTime? completedAt)
    {
        if (context.Ids.Find(table, legacyId) is not null)
        {
            return;
        }

        if (context.Ids.Find(TypesTable, legacyTypeId) is not { } typeId)
        {
            context.Report.Skip(table, legacyId, "import type not migrated");
            return;
        }

        var file = string.IsNullOrWhiteSpace(fileName) ? "legacy.xlsx" : fileName;
        var job = ImportJob.ImportLegacy(
            context.Ids.NewId(), typeId, LegacyText.Fit(name, ImportJob.NameMaxLength, out _), file, Status(status), rows, error is null ? null : LegacyText.Fit(error, 500, out _),
            LegacyImportContext.Instant(createdAt), startedAt is { } started ? LegacyImportContext.Instant(started) : null,
            completedAt is { } completed ? LegacyImportContext.Instant(completed) : null);
        context.Tenant.Add(job);
        context.Ids.Add(table, legacyId, job.Id);
        result.Created++;
    }
}
