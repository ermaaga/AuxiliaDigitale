using Auxilia.Domain.Directory;

namespace Auxilia.Application.Abstractions.Directory;

/// <summary>Sortable columns of the employee list (same as the client lists; default surname, name).</summary>
public enum EmployeeSort
{
    LastName,
    FullName,
    Email,
    UserName,
}

/// <summary>A page request of the employee list, already validated; the text filters match anywhere, case-insensitive.</summary>
public sealed record EmployeeFilter(
    string? FullName,
    string? LastName,
    string? Email,
    string? UserName,
    string? Phone,
    bool? CanSignIn,
    EmployeeSort Sort,
    bool Descending,
    int Skip,
    int Take);

/// <summary>An employee as the list shows it (person, account, profile and the number of clients in charge).</summary>
public sealed record EmployeeRow(
    Guid UserId,
    string FirstName,
    string LastName,
    string? Email,
    string UserName,
    string? Phone,
    bool CanSignIn,
    bool IsDefault,
    int AssignedClients,
    string? ImageVersion);

/// <summary>A specialization held by an employee.</summary>
public sealed record EmployeeSpecializationRow(Guid UserId, Guid SpecializationId, string Name);

/// <summary>
/// Employees of the current tenant (F06): users with the Employee role whose person is not deleted, with their
/// optional <see cref="EmployeeProfile"/>. One unit of work (inside a write operation it joins the operation's
/// transaction).
/// </summary>
public interface IEmployeeData : IAsyncDisposable
{
    Task<(IReadOnlyList<EmployeeRow> Items, int Total)> PageAsync(EmployeeFilter filter, CancellationToken cancellationToken);

    /// <summary>The active Employee specializations of the users, by name.</summary>
    Task<IReadOnlyList<EmployeeSpecializationRow>> SpecializationsOfAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>The person of the user (tracked) when the user is an employee and the person is not deleted.</summary>
    Task<Person?> FindPersonAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The profile of an employee (tracked), <c>null</c> when it has none yet.</summary>
    Task<EmployeeProfile?> FindProfileAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The profile flagged as default (tracked), if any.</summary>
    Task<EmployeeProfile?> FindDefaultAsync(CancellationToken cancellationToken);

    /// <summary>Creation time of the user account.</summary>
    Task<DateTimeOffset?> CreatedAtAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The client profiles in charge of the employee (tracked, with their assignments).</summary>
    Task<IReadOnlyList<ClientProfile>> ClientsInChargeAsync(Guid userId, CancellationToken cancellationToken);

    Task<int> CountClientsInChargeAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Another person not deleted has the fiscal code.</summary>
    Task<bool> FiscalCodeTakenAsync(string fiscalCode, Guid? exceptPersonId, CancellationToken cancellationToken);

    /// <summary>Users with the Administrator role who can sign in, by name: who an employee can report to (Q32).</summary>
    Task<IReadOnlyList<EmployeeName>> AdministratorsAsync(CancellationToken cancellationToken);

    /// <summary>Names of the users (person first and last name), deleted people included.</summary>
    Task<IReadOnlyList<EmployeeName>> NamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>The profile picture hash of the user (F04), <c>null</c> without a picture.</summary>
    Task<string?> ImageVersionAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Active specializations of the Employee role (tracked, with members).</summary>
    Task<IReadOnlyList<Specialization>> EmployeeSpecializationsAsync(CancellationToken cancellationToken);

    void Add(Person person);

    void Add(EmployeeProfile profile);

    /// <summary>Soft delete of the person: the employee disappears with it; the profile stays.</summary>
    void Remove(Person person);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IEmployeeDataFactory
{
    Task<IEmployeeData> OpenAsync(CancellationToken cancellationToken);
}
