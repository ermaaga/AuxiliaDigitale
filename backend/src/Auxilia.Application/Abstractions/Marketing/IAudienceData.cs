using Auxilia.Domain.Marketing;

namespace Auxilia.Application.Abstractions.Marketing;

/// <summary>Whose clients an audience may contain: every client, or only those in charge of an employee (N01).</summary>
public sealed record AudienceScope(Guid? EmployeeUserId)
{
    public static readonly AudienceScope Everyone = new((Guid?)null);
}

/// <summary>A client of an audience (preview, members).</summary>
public sealed record AudienceMemberRow(Guid Id, string FirstName, string LastName, string? Email, Domain.Directory.ClientStatus Status);

public sealed record SegmentRow(Guid Id, string Name, string? Description, string Rule, DateTimeOffset UpdatedAt);

public sealed record StaticListRow(Guid Id, string Name, string? Description, int MemberCount, DateTimeOffset UpdatedAt);

/// <summary>Segments and static lists of the current tenant (N01, M-02), one unit of work; deleted clients never count.</summary>
public interface IAudienceData : IAsyncDisposable
{
    /// <summary>The clients the rule selects within the scope, by name; <paramref name="today"/> in the tenant time zone (ages).</summary>
    Task<(IReadOnlyList<AudienceMemberRow> Items, int Total)> SegmentMembersAsync(
        SegmentRule rule, AudienceScope scope, DateOnly today, int skip, int take, CancellationToken cancellationToken);

    /// <summary>Every client id the rule selects (campaign recipients, M-03).</summary>
    Task<IReadOnlyList<Guid>> SegmentMemberIdsAsync(SegmentRule rule, AudienceScope scope, DateOnly today, CancellationToken cancellationToken);

    Task<IReadOnlyList<SegmentRow>> SegmentsAsync(CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<Segment?> FindSegmentAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SegmentNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>Lists with the members within the scope.</summary>
    Task<IReadOnlyList<StaticListRow>> ListsAsync(AudienceScope scope, CancellationToken cancellationToken);

    /// <summary>Tracked.</summary>
    Task<StaticList?> FindListAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ListNameTakenAsync(string name, Guid? exceptId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<AudienceMemberRow> Items, int Total)> ListMembersAsync(Guid listId, AudienceScope scope, int skip, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> ListMemberIdsAsync(Guid listId, AudienceScope scope, CancellationToken cancellationToken);

    /// <summary>The clients (not deleted, within the scope) among <paramref name="ids"/>.</summary>
    Task<IReadOnlyList<Guid>> ClientsInScopeAsync(IReadOnlyCollection<Guid> ids, AudienceScope scope, CancellationToken cancellationToken);

    /// <summary>The members already in the list among <paramref name="ids"/>.</summary>
    Task<IReadOnlyList<Guid>> MembersAmongAsync(Guid listId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<int> RemoveMembersAsync(Guid listId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    void Add(Segment segment);

    void Remove(Segment segment);

    void Add(StaticList list);

    void Remove(StaticList list);

    void AddMembers(IEnumerable<StaticListMember> members);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IAudienceDataFactory
{
    Task<IAudienceData> OpenAsync(CancellationToken cancellationToken);
}
