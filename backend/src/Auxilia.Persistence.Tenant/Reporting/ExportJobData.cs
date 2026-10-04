using Auxilia.Application.Abstractions.Exports;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Reporting;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Reporting;

internal sealed class ExportJobDataFactory(ITenantDbContextFactory databases) : IExportJobDataFactory
{
    public async Task<IExportJobData> OpenAsync(CancellationToken cancellationToken) => new ExportJobData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IExportJobData"/>
internal sealed class ExportJobData(ITenantDbContext db) : IExportJobData
{
    public Task<ExportJob?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<ExportJob>().SingleOrDefaultAsync(job => job.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ExportJobRow>> OfUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await db.Set<ExportJob>().AsNoTracking()
            .Where(job => job.UserId == userId && job.ExpiresAt > now)
            .OrderByDescending(job => job.CreatedAt)
            .Select(job => new ExportJobRow(job.Id, job.SourceKey, job.Format, job.Status, job.FileName, job.RowCount, job.ErrorCode, job.CreatedAt, job.ExpiresAt))
            .ToListAsync(cancellationToken);

    public Task<int> DeleteExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Set<ExportJob>().Where(job => job.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);

    public void Add(ExportJob job) => db.Set<ExportJob>().Add(job);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
