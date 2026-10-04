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
/// Which cases a caller may see (F10, D-04), applied in the query: everything, the cases an employee may see (no
/// specialization, a non-private one or one held), or the cases of one client. Nothing when none is set.
/// </summary>
/// <summary>A case event of the client timeline: a status change (<paramref name="ToStatus"/>) or a payment (<paramref name="Amount"/>).</summary>
public sealed record CaseTimelineRow(
    Guid CaseId, string Number, string ServiceName, DateTimeOffset At, CaseStatus? FromStatus, CaseStatus? ToStatus, bool IsRejected, decimal? Amount, string Currency, Guid? ActorUserId);

public sealed record CaseScope(bool Everything, Guid? EmployeeUserId, Guid? ClientId)
{
    public static readonly CaseScope None = new(false, null, null);
}

/// <summary>Sortable columns of the case lists (legacy: client, service, start, end, amount; default start descending).</summary>
public enum CaseSort
{
    StartedOn,
    Client,
    Service,
    ExpiresOn,
    AmountPaid,
    Number,
}

/// <summary>
/// A page request of the case lists, already validated. <see cref="OnlyHeldOrUnspecialized"/> is the employee's
/// "show all" off: only cases without a specialization or with one the employee holds.
/// </summary>
public sealed record CaseFilter(
    CaseScope Scope,
    string? ClientName,
    string? ServiceName,
    Guid? ClientId,
    Guid? ServiceId,
    CaseStatus? Status,
    bool OnlyHeldOrUnspecialized,
    bool IncludeCompleted,
    CaseSort Sort,
    bool Descending,
    int Skip,
    int Take);

/// <summary>A case as the lists show it.</summary>
public sealed record CaseRow(
    Guid Id,
    string Number,
    Guid ClientId,
    string ClientName,
    Guid ServiceId,
    string ServiceName,
    Guid? SpecializationId,
    string? SpecializationName,
    bool SpecializationPrivate,
    CaseStatus Status,
    bool IsRejected,
    bool IsActive,
    DateOnly StartedOn,
    DateOnly? DueOn,
    DateOnly? ExpiresOn,
    decimal Price,
    string Currency,
    decimal AmountPaid,
    string CustomFields);

/// <summary>
/// Cases of the current tenant (F09), one unit of work (inside a write operation it joins the operation's
/// transaction). Deleted cases are never returned.
/// </summary>
/// <summary>A case on the dashboards (due soon, the client's active case).</summary>
public sealed record CaseDashboardItem(Guid Id, string Number, string ClientName, string ServiceName, DateOnly? DueOn);

/// <summary>
/// The case figures of a dashboard (F27) within a <see cref="CaseScope"/>: open cases, cases per service and money
/// received per month of start since <c>from</c>, open cases due between today and the horizon (soonest first), the
/// latest open case.
/// </summary>
public sealed record CaseDashboard(
    int Open,
    IReadOnlyList<(string Service, int Count)> PerService,
    IReadOnlyList<(int Year, int Month, decimal Amount)> Revenue,
    IReadOnlyList<CaseDashboardItem> DueSoon,
    CaseDashboardItem? Latest);

public interface ICaseData : IAsyncDisposable
{
    Task<CaseDashboard> DashboardAsync(CaseScope scope, DateOnly? from, DateOnly today, DateOnly horizon, int take, CancellationToken cancellationToken);

    /// <summary>Cases of clients not deleted, within <see cref="CaseFilter.Scope"/>.</summary>
    Task<(IReadOnlyList<CaseRow> Items, int Total)> PageAsync(CaseFilter filter, CancellationToken cancellationToken);

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

    /// <summary>Active cases (clients not deleted) whose expiry date is <paramref name="today"/> or earlier (tracked).</summary>
    Task<IReadOnlyList<Case>> ExpiredActiveAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>Active open cases ending (expiry, else due date) between tomorrow and <paramref name="horizon"/> (tracked).</summary>
    Task<IReadOnlyList<Case>> ExpiringAsync(DateOnly today, DateOnly horizon, CancellationToken cancellationToken);

    /// <summary>The user of each client person (clients without an account are missing).</summary>
    Task<IReadOnlyDictionary<Guid, Guid>> ClientUsersAsync(IReadOnlyCollection<Guid> clientIds, CancellationToken cancellationToken);

    /// <summary>
    /// The status changes and payments of the client's cases the scope sees (F10), newest first, before an instant
    /// (client timeline, B-26).
    /// </summary>
    Task<IReadOnlyList<CaseTimelineRow>> TimelineAsync(CaseScope scope, Guid clientId, DateTimeOffset? before, int take, CancellationToken cancellationToken);

    void Add(Case @case);

    /// <summary>Soft delete.</summary>
    void Remove(Case @case);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ICaseDataFactory
{
    Task<ICaseData> OpenAsync(CancellationToken cancellationToken);
}
