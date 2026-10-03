using System.Text.RegularExpressions;

using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Directory;

/// <summary>The personal data of a client or employee, as staff enter it.</summary>
public sealed record PersonDetails(
    string? FirstName,
    string? LastName,
    string? Email,
    DateOnly? BirthDate,
    string? Phone,
    string? FiscalCode);

/// <summary>
/// A person known to the tenant (<c>directory.people</c>): the common part of clients and employees. A user account
/// (<c>identity.users</c>) belongs to one person. Deletes are soft (Q29). Phone and fiscal code follow one rule
/// everywhere (Q53, Q54); custom fields are a JSON object validated by the Configuration module (F20).
/// </summary>
public sealed partial class Person : AggregateRoot<Guid>, IAuditable, ISoftDeletable
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 256;
    public const int PhoneMaxLength = 20;
    public const int FiscalCodeLength = 16;

    /// <summary>Oldest birth date accepted (typing errors such as 0198).</summary>
    public static readonly DateOnly MinBirthDate = new(1900, 1, 1);

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

    private Person(Guid id)
        : base(id)
    {
        FirstName = LastName = string.Empty;
    }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    public string? Email { get; private set; }

    public DateOnly? BirthDate { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>Italian fiscal code, upper case (omocodia accepted); unique among the people not deleted.</summary>
    public string? FiscalCode { get; private set; }

    /// <summary>Custom field values (JSON object, <c>custom_fields jsonb</c>), already validated (F20).</summary>
    public string CustomFields { get; private set; } = "{}";

    public string FullName => $"{FirstName} {LastName}";

    public static Result<Person> Create(Guid id, PersonDetails details, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(details);

        var person = new Person(id);
        var updated = person.Update(details, today);
        return updated.IsSuccess ? person : Result.Failure<Person>(updated.Error!);
    }

    /// <summary>Checks every value (all errors at once, one per field) and replaces the personal data.</summary>
    public Result Update(PersonDetails details, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(details);

        var errors = Validate(details, today);
        if (errors.Count > 0)
        {
            return Errors.Directory.PersonInvalid(errors);
        }

        FirstName = Text(details.FirstName)!;
        LastName = Text(details.LastName)!;
        Email = Text(details.Email);
        BirthDate = details.BirthDate;
        Phone = Text(details.Phone);
        FiscalCode = NormalizeFiscalCode(details.FiscalCode);
        return Result.Success();
    }

    /// <summary>The errors of the person rules (one per field, translation keys), empty when every value is valid.</summary>
    public static Dictionary<string, string[]> Validate(PersonDetails details, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(details);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var firstName = Text(details.FirstName);
        var lastName = Text(details.LastName);
        var email = Text(details.Email);
        var phone = Text(details.Phone);
        var fiscalCode = NormalizeFiscalCode(details.FiscalCode);

        if (firstName is null || firstName.Length > NameMaxLength)
        {
            errors["firstName"] = ["validation.person.firstName"];
        }

        if (lastName is null || lastName.Length > NameMaxLength)
        {
            errors["lastName"] = ["validation.person.lastName"];
        }

        if (email is not null && (email.Length > EmailMaxLength || !EmailPattern().IsMatch(email)))
        {
            errors["email"] = ["validation.person.email"];
        }

        if (details.BirthDate is { } birthDate && (birthDate < MinBirthDate || birthDate > today))
        {
            errors["birthDate"] = ["validation.person.birthDate"];
        }

        if (phone is not null && !PhonePattern().IsMatch(phone))
        {
            errors["phone"] = ["validation.person.phone"];
        }

        if (fiscalCode is not null && !IsFiscalCode(fiscalCode))
        {
            errors["fiscalCode"] = ["validation.person.fiscalCode"];
        }

        return errors;
    }

    public void SetCustomFields(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        CustomFields = json;
    }

    /// <summary>Upper case without spaces; <c>null</c> when empty.</summary>
    public static string? NormalizeFiscalCode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>Q54: 16 characters, month letter, and the digits that omocodia may replace with letters.</summary>
    public static bool IsFiscalCode(string value) => FiscalCodePattern().IsMatch(value);

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Z]{6}[0-9LMNPQRSTUV]{2}[ABCDEHLMPRST][0-9LMNPQRSTUV]{2}[A-Z][0-9LMNPQRSTUV]{3}[A-Z]$", RegexOptions.CultureInvariant)]
    private static partial Regex FiscalCodePattern();

    // Q53: one rule for staff forms, registration and import.
    [GeneratedRegex(@"^[\d\s+\-()]{8,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
