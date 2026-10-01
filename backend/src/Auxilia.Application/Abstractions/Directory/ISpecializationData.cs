using Auxilia.Domain.Directory;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Abstractions.Directory;

/// <summary>A user account of the tenant with the name of its person (members and candidates of a specialization).</summary>
public sealed record DirectoryUser(Guid UserId, string UserName, string? FullName, string? Email, bool IsActive);

/// <summary>
/// Role specializations of the current tenant (F12), one unit of work (inside a write operation it joins the
/// operation's transaction). Only active specializations are read.
/// </summary>
public interface ISpecializationData : IAsyncDisposable
{
    /// <summary>Active specializations with their members, by role then name.</summary>
    Task<IReadOnlyList<Specialization>> ListAsync(TenantRole? role, CancellationToken cancellationToken);

    /// <summary>The active specialization (tracked, with its members).</summary>
    Task<Specialization?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Another active specialization of the role has the name (case-insensitive).</summary>
    Task<bool> NameTakenAsync(TenantRole role, string name, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>The users holding the specialization, by user name.</summary>
    Task<IReadOnlyList<DirectoryUser>> MembersAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Users of the role not holding the specialization, matching <paramref name="search"/> (user name, name, e-mail), by user name.</summary>
    Task<IReadOnlyList<DirectoryUser>> CandidatesAsync(Guid id, TenantRole role, string? search, int limit, CancellationToken cancellationToken);

    /// <summary>The ids among <paramref name="userIds"/> of existing users with the role.</summary>
    Task<IReadOnlyList<Guid>> UsersWithRoleAsync(IReadOnlyCollection<Guid> userIds, TenantRole role, CancellationToken cancellationToken);

    void Add(Specialization specialization);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface ISpecializationDataFactory
{
    Task<ISpecializationData> OpenAsync(CancellationToken cancellationToken);
}
