using Auxilia.Domain.Directory;

namespace Auxilia.Application.Abstractions.Directory;

/// <summary>Sortable columns of the client lists (legacy: full name, surname, e-mail, user name; default surname, name).</summary>
public enum ClientSort
{
    LastName,
    FullName,
    Email,
    UserName,
}

/// <summary>
/// A page request of the client lists, already validated. <see cref="EmployeeUserId"/> restricts to the clients of
/// that employee ("my clients"); the text filters match anywhere, case-insensitive.
/// </summary>
public sealed record ClientFilter(
    Guid? EmployeeUserId,
    string? FullName,
    string? LastName,
    string? Email,
    string? UserName,
    string? Phone,
    ClientStatus? Status,
    ClientSort Sort,
    bool Descending,
    int Skip,
    int Take);

/// <summary>A client as the lists show it (person, profile, account and employee name in one row).</summary>
public sealed record ClientRow(
    Guid Id,
    string FirstName,
    string LastName,
    string? Email,
    string UserName,
    string? Phone,
    string? FiscalCode,
    ClientStatus Status,
    bool CanSignIn,
    Guid? EmployeeUserId,
    string? EmployeeName,
    string CustomFields);

/// <summary>A user with the Employee role who can sign in, with the name of its person.</summary>
public sealed record EmployeeName(Guid UserId, string FullName);

/// <summary>
/// Clients of the current tenant (F05): people with a client profile, one unit of work (inside a write operation it
/// joins the operation's transaction). Deleted people and profiles are never returned.
/// </summary>
public interface IClientData : IAsyncDisposable
{
    Task<(IReadOnlyList<ClientRow> Items, int Total)> PageAsync(ClientFilter filter, CancellationToken cancellationToken);

    /// <summary>The person of a client (tracked); <c>null</c> when it is not a client.</summary>
    Task<Person?> FindPersonAsync(Guid clientId, CancellationToken cancellationToken);

    /// <summary>The client profile with its assignments (tracked); <c>null</c> when the person is deleted.</summary>
    Task<ClientProfile?> FindProfileAsync(Guid clientId, CancellationToken cancellationToken);

    /// <summary>Another person not deleted has the fiscal code.</summary>
    Task<bool> FiscalCodeTakenAsync(string fiscalCode, Guid? exceptPersonId, CancellationToken cancellationToken);

    /// <summary>Names of the users (person first and last name), for the ids that exist.</summary>
    Task<IReadOnlyList<EmployeeName>> NamesAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Users with the Employee role who can sign in, by name: who a client can be assigned to.</summary>
    Task<IReadOnlyList<EmployeeName>> AssignableEmployeesAsync(CancellationToken cancellationToken);

    /// <summary>Active specializations of the Client role (tracked, with members).</summary>
    Task<IReadOnlyList<Specialization>> ClientSpecializationsAsync(CancellationToken cancellationToken);

    void Add(Person person);

    void Add(ClientProfile profile);

    /// <summary>Soft delete of the person (Q29): the client disappears with it; profile and history stay.</summary>
    void Remove(Person person);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IClientDataFactory
{
    Task<IClientData> OpenAsync(CancellationToken cancellationToken);
}
