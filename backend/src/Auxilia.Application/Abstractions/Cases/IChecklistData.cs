using Auxilia.Domain.Cases;

namespace Auxilia.Application.Abstractions.Cases;

/// <summary>An item of the checklist of a case: the service item and, when ticked, who and when.</summary>
public sealed record CaseChecklistRow(Guid ItemId, string Name, Guid? FolderId, bool Required, DateTimeOffset? CheckedAt, Guid? CheckedByUserId);

/// <summary>Document checklists of the services and their ticks on the cases (B-26), one unit of work.</summary>
public interface IChecklistData : IAsyncDisposable
{
    /// <summary>Tracked, in order.</summary>
    Task<IReadOnlyList<ServiceChecklistItem>> ItemsAsync(Guid serviceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CaseChecklistRow>> OfCaseAsync(Guid caseId, Guid serviceId, CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<CaseChecklistMark?> FindMarkAsync(Guid caseId, Guid itemId, CancellationToken cancellationToken);

    void Add(ServiceChecklistItem item);

    void Remove(ServiceChecklistItem item);

    void Add(CaseChecklistMark mark);

    void Remove(CaseChecklistMark mark);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IChecklistDataFactory
{
    Task<IChecklistData> OpenAsync(CancellationToken cancellationToken);
}
