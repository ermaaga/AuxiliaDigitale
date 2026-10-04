using Auxilia.Application.Abstractions.Imports;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Imports;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Imports;

internal sealed class ImportDataFactory(ITenantDbContextFactory databases) : IImportDataFactory
{
    public async Task<IImportData> OpenAsync(CancellationToken cancellationToken) => new ImportData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IImportData"/>
internal sealed class ImportData(ITenantDbContext db) : IImportData
{
    public async Task<IReadOnlyList<ImportTypeRow>> TypesAsync(CancellationToken cancellationToken) =>
        await db.Set<ImportType>().AsNoTracking()
            .OrderBy(type => type.Name)
            .Select(type => new ImportTypeRow(
                type.Id, type.Name, type.TargetEntity, type.CreatedAt, db.Set<ImportJob>().Count(job => job.ImportTypeId == type.Id)))
            .ToListAsync(cancellationToken);

    public Task<ImportType?> FindTypeAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<ImportType>().SingleOrDefaultAsync(type => type.Id == id, cancellationToken);

    public Task<bool> TypeHasJobsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<ImportJob>().AnyAsync(job => job.ImportTypeId == id, cancellationToken);

    public async Task<(IReadOnlyList<ImportJobListRow> Items, int Total)> JobsAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var jobs = Rows();
        var total = await jobs.CountAsync(cancellationToken);
        var items = await jobs.OrderByDescending(job => job.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<ImportJobListRow?> JobAsync(Guid id, CancellationToken cancellationToken) =>
        Rows().SingleOrDefaultAsync(job => job.Id == id, cancellationToken);

    public Task<ImportJob?> FindJobAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<ImportJob>().SingleOrDefaultAsync(job => job.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<ImportJobRowData> Items, int Total)> RowsAsync(
        Guid jobId, ImportRowStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var rows = db.Set<ImportJobRow>().AsNoTracking().Where(row => row.JobId == jobId);
        if (status is { } wanted)
        {
            rows = rows.Where(row => row.Status == wanted);
        }

        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderBy(row => row.RowNumber).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(row => new ImportJobRowData(row.RowNumber, row.Status, row.Data, row.Errors, row.EntityId))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<IReadOnlyList<ImportJobRow>> ValidRowsAsync(Guid jobId, int afterRow, int take, CancellationToken cancellationToken) =>
        await db.Set<ImportJobRow>()
            .Where(row => row.JobId == jobId && row.Status == ImportRowStatus.Valid && row.RowNumber > afterRow)
            .OrderBy(row => row.RowNumber)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task DeleteRowsAsync(Guid jobId, CancellationToken cancellationToken) =>
        db.Set<ImportJobRow>().Where(row => row.JobId == jobId).ExecuteDeleteAsync(cancellationToken);

    public void Add(ImportType type) => db.Set<ImportType>().Add(type);

    public void Remove(ImportType type) => db.Set<ImportType>().Remove(type);

    public void Add(ImportJob job) => db.Set<ImportJob>().Add(job);

    public void Remove(ImportJob job) => db.Set<ImportJob>().Remove(job);

    public void AddRows(IEnumerable<ImportJobRow> rows) => db.Set<ImportJobRow>().AddRange(rows);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();

    private IQueryable<ImportJobListRow> Rows() =>
        from job in db.Set<ImportJob>().AsNoTracking()
        join type in db.Set<ImportType>() on job.ImportTypeId equals type.Id
        select new ImportJobListRow(
            job.Id, type.Id, type.Name, type.TargetEntity, job.Name, job.FileName, job.Status, job.TotalRows, job.ProcessedRows,
            job.SuccessRows, job.FailedRows, job.ErrorCode, job.ErrorMessage, job.CreatedAt, job.StartedAt, job.CompletedAt);
}
