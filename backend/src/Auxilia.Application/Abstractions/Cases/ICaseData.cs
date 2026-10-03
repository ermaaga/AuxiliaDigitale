using Auxilia.Domain.Cases;

namespace Auxilia.Application.Abstractions.Cases;

/// <summary>The service as a new case needs it (not deleted).</summary>
public sealed record CaseService(Guid Id, string Name, decimal Price, string Currency, Guid? SpecializationId, bool IsActive);

/// <summary>The names around a case (deleted service and people included: the case keeps showing them).</summary>
public sealed record CaseNames(
    string ClientName,
    string ServiceName,
    string? ServiceDescription,
    int ServiceDurationDays,
    string? SpecializationName,
    bool SpecializationPrivate);

/// <summary>What the access rules know of the caller (F10): the person of the user and the specializations held.</summary>
public sealed record CaseCaller(Guid? PersonId, IReadOnlySet<Guid> SpecializationIds);

/// <summary>
/// Cases of the current tenant (F09), one unit of work (inside a write operation it joins the operation's
/// transaction). Deleted cases are never returned.
/// </summary>
public interface ICaseData : IAsyncDisposable
{
    /// <summary>The next number of the year (a counter row locked until the transaction ends).</summary>
    Task<int> NextNumberAsync(int year, CancellationToken cancellationToken);

    /// <summary>The case with history and payments; tracked unless <paramref name="readOnly"/>.</summary>
    Task<Case?> FindAsync(Guid id, bool readOnly, CancellationToken cancellationToken);

    Task<CaseNames> NamesAsync(Case @case, CancellationToken cancellationToken);

    /// <summary>Names of the users (person first and last name), deleted people included.</summary>
    Task<IReadOnlyDictionary<Guid, string>> UserNamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    Task<CaseService?> ServiceAsync(Guid serviceId, CancellationToken cancellationToken);

    /// <summary>Whether the specialization is private; <c>null</c> when it is not an active Employee specialization.</summary>
    Task<bool?> EmployeeSpecializationPrivacyAsync(Guid specializationId, CancellationToken cancellationToken);

    /// <summary>Whether the specialization is private, active or not (an existing case keeps it).</summary>
    Task<bool> IsPrivateSpecializationAsync(Guid specializationId, CancellationToken cancellationToken);

    Task<CaseCaller> CallerAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Q03: the client has a case still open on <paramref name="today"/> (<see cref="Case.CountsAsOpen"/>).</summary>
    Task<bool> HasOpenCasesAsync(Guid clientId, DateOnly today, CancellationToken cancellationToken);

    void Add(Case @case);

    /// <summary>Soft delete.</summary>
    void Remove(Case @case);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ICaseDataFactory
{
    Task<ICaseData> OpenAsync(CancellationToken cancellationToken);
}
