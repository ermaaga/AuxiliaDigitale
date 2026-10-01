namespace Auxilia.Contracts.Directory;

/// <summary>
/// A specialization of the <c>Client</c> or <c>Employee</c> role (F12). <see cref="IsPrivate"/> makes the cases of its
/// services private (F10).
/// </summary>
public sealed record SpecializationResponse(
    Guid Id,
    string Name,
    string Role,
    string? Description,
    string? Email,
    string? WorkPhone,
    bool IsPrivate,
    int MemberCount);

public sealed record CreateSpecializationRequest(string Name, string Role, string? Description, string? Email, string? WorkPhone, bool IsPrivate);

/// <summary>The role never changes: the members hold it.</summary>
public sealed record UpdateSpecializationRequest(string Name, string? Description, string? Email, string? WorkPhone, bool IsPrivate);

public sealed record CreateSpecializationResponse(Guid Id);

/// <summary>A user of the tenant holding (or able to hold) a specialization.</summary>
public sealed record SpecializationMemberResponse(Guid UserId, string UserName, string? FullName, string? Email, bool IsActive);

/// <summary>Users of the specialization's role to add (users already assigned are ignored).</summary>
public sealed record AddSpecializationMembersRequest(IReadOnlyList<Guid> UserIds);
