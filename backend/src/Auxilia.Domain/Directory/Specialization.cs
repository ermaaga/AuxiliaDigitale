using System.Text.RegularExpressions;

using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Domain.Directory;

/// <summary>What the System edits on a specialization; the role never changes after creation.</summary>
public sealed record SpecializationSpec(string Name, string? Description, string? Email, string? WorkPhone, bool IsPrivate);

/// <summary>
/// A specialization of the <see cref="TenantRole.Client"/> or <see cref="TenantRole.Employee"/> role
/// (<c>directory.specializations</c>, F12, D-18): managed by the System, held by many users of that role (Q30). A
/// private specialization makes the cases of its services private (F10). Deleting it deactivates it (legacy
/// <c>IsActive</c>): it keeps its members and disappears from the lists.
/// </summary>
public sealed partial class Specialization : AggregateRoot<Guid>, IAuditable
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;
    public const int EmailMaxLength = 256;
    public const int WorkPhoneMaxLength = 50;

    /// <summary>The roles a specialization can belong to (Q34: a fixed list, not the roles currently in use).</summary>
    public static readonly IReadOnlyList<TenantRole> Roles = [TenantRole.Client, TenantRole.Employee];

    private readonly List<SpecializationMember> members = [];

    private Specialization(Guid id, TenantRole role)
        : base(id)
    {
        Role = role;
        Name = string.Empty;
        IsActive = true;
    }

    private Specialization()
    {
        Name = string.Empty;
    }

    public string Name { get; private set; }

    public TenantRole Role { get; private set; }

    public string? Description { get; private set; }

    public string? Email { get; private set; }

    /// <summary>Legacy <c>WorkNumber</c>.</summary>
    public string? WorkPhone { get; private set; }

    /// <summary>Legacy <c>PrivateSubscriptions</c>: the cases of its services are private (F10).</summary>
    public bool IsPrivate { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<SpecializationMember> Members => members.AsReadOnly();

    public static Result<Specialization> Create(Guid id, TenantRole role, SpecializationSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (!Roles.Contains(role))
        {
            return Errors.Directory.SpecializationInvalid("role", "validation.specializations.role");
        }

        var specialization = new Specialization(id, role);
        var applied = specialization.Apply(spec);
        return applied.IsFailure ? Result.Failure<Specialization>(applied.Error!) : specialization;
    }

    public Result Update(SpecializationSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return Apply(spec);
    }

    public void Deactivate() => IsActive = false;

    /// <summary>Adds the users that are not members yet; returns how many were added.</summary>
    public int AddMembers(IEnumerable<Guid> userIds, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        var added = 0;
        foreach (var userId in userIds.Distinct().Where(userId => members.All(member => member.UserId != userId)))
        {
            members.Add(new SpecializationMember(Id, userId, at));
            added++;
        }

        return added;
    }

    public bool RemoveMember(Guid userId) => members.RemoveAll(member => member.UserId == userId) > 0;

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Result Apply(SpecializationSpec spec)
    {
        var name = spec.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > NameMaxLength)
        {
            return Errors.Directory.SpecializationInvalid("name", "validation.specializations.name");
        }

        var description = Optional(spec.Description);
        if (description is { Length: > DescriptionMaxLength })
        {
            return Errors.Directory.SpecializationInvalid("description", "validation.specializations.description");
        }

        var email = Optional(spec.Email);
        if (email is not null && (email.Length > EmailMaxLength || !EmailPattern().IsMatch(email)))
        {
            return Errors.Directory.SpecializationInvalid("email", "validation.specializations.email");
        }

        var phone = Optional(spec.WorkPhone);
        if (phone is not null && (phone.Length > WorkPhoneMaxLength || !PhonePattern().IsMatch(phone)))
        {
            return Errors.Directory.SpecializationInvalid("workPhone", "validation.specializations.workPhone");
        }

        Name = name;
        Description = description;
        Email = email;
        WorkPhone = phone;
        IsPrivate = spec.IsPrivate;
        return Result.Success();
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^[0-9+()./ -]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}

/// <summary>A user holding a specialization (<c>directory.specialization_members</c>, legacy <c>UserRoleSpecializations</c>).</summary>
public sealed class SpecializationMember
{
    internal SpecializationMember(Guid specializationId, Guid userId, DateTimeOffset assignedAt)
    {
        SpecializationId = specializationId;
        UserId = userId;
        AssignedAt = assignedAt;
    }

    private SpecializationMember()
    {
    }

    public Guid SpecializationId { get; private set; }

    public Guid UserId { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }
}
