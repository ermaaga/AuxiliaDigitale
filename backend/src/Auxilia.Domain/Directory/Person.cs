using Auxilia.SharedKernel.Domain;

namespace Auxilia.Domain.Directory;

/// <summary>
/// A person known to the tenant (<c>directory.people</c>): the common part of clients and employees. Minimal for now
/// (P2-01): the Directory module extends it with profiles, fiscal code and custom fields (B-01). A user account
/// (<c>identity.users</c>) belongs to one person.
/// </summary>
public sealed class Person : AggregateRoot<Guid>, IAuditable, ISoftDeletable
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 256;

    public Person(Guid id, string firstName, string lastName, string? email)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstName.Trim().Length, NameMaxLength);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lastName.Trim().Length, NameMaxLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(email?.Trim().Length ?? 0, EmailMaxLength);

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }

    private Person()
    {
        FirstName = LastName = string.Empty;
    }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public string? Email { get; private set; }
}
