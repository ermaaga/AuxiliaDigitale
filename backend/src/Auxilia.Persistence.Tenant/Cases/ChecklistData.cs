using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Domain.Cases;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.Persistence.Tenant.Cases;

internal sealed class ChecklistDataFactory(ITenantDbContextFactory databases) : IChecklistDataFactory
{
    public async Task<IChecklistData> OpenAsync(CancellationToken cancellationToken) => new ChecklistData(await databases.CreateAsync(cancellationToken));
}

/// <inheritdoc cref="IChecklistData"/>
internal sealed class ChecklistData(ITenantDbContext db) : IChecklistData
{
    public async Task<IReadOnlyList<ServiceChecklistItem>> ItemsAsync(Guid serviceId, CancellationToken cancellationToken) =>
        await db.Set<ServiceChecklistItem>().Where(item => item.ServiceId == serviceId).OrderBy(item => item.Order).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CaseChecklistRow>> OfCaseAsync(Guid caseId, Guid serviceId, CancellationToken cancellationToken) =>
        await (from item in db.Set<ServiceChecklistItem>().AsNoTracking()
               where item.ServiceId == serviceId
               join mark in db.Set<CaseChecklistMark>().Where(mark => mark.CaseId == caseId) on item.Id equals mark.ItemId into marks
               from mark in marks.DefaultIfEmpty()
               orderby item.Order
               select new CaseChecklistRow(
                   item.Id, item.Name, item.FolderId, item.IsRequired, mark == null ? null : mark.CheckedAt, mark == null ? null : mark.CheckedByUserId))
            .ToListAsync(cancellationToken);

    public Task<CaseChecklistMark?> FindMarkAsync(Guid caseId, Guid itemId, CancellationToken cancellationToken) =>
        db.Set<CaseChecklistMark>().SingleOrDefaultAsync(mark => mark.CaseId == caseId && mark.ItemId == itemId, cancellationToken);

    public void Add(ServiceChecklistItem item) => db.Set<ServiceChecklistItem>().Add(item);

    public void Remove(ServiceChecklistItem item) => db.Set<ServiceChecklistItem>().Remove(item);

    public void Add(CaseChecklistMark mark) => db.Set<CaseChecklistMark>().Add(mark);

    public void Remove(CaseChecklistMark mark) => db.Set<CaseChecklistMark>().Remove(mark);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => db.DisposeAsync();
}
