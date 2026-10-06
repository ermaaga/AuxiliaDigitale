using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Directory;

/// <summary>Where a registration request is (Q07: an explicit status, not a text in the notes).</summary>
public enum RegistrationStatus
{
    /// <summary>Waiting for staff: at most one per e-mail (Q05).</summary>
    Pending,

    /// <summary>A client was created from it (terminal).</summary>
    Approved,

    /// <summary>Refused by staff, no client created (terminal).</summary>
    Rejected,
}

/// <summary>What an applicant sends (F02); the privacy consent is mandatory and stored with its version (Q08).</summary>
public sealed record RegistrationDetails(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Phone,
    DateOnly? BirthDate,
    string? FiscalCode,
    bool PrivacyConsent,
    string? PrivacyVersion);

/// <summary>
/// A request to become a client, sent by a registered external client application (<c>directory.registration_requests</c>,
/// F02/F03, API only D-14). Every personal field is required and follows the person rules (Q53, Q54); the applicant
/// must be at least <c>registration.minimumAge</c> years old (Q59). Staff approve it (a client is created) or reject
/// it, once (F03).
/// </summary>
public sealed class RegistrationRequest : AggregateRoot<Guid>, IAuditable
{
    public const int LanguageMaxLength = 10;
    public const int PrivacyVersionMaxLength = 50;
    public const int NotesMaxLength = 500;

    private RegistrationRequest(Guid id, string clientApplication, string language, DateTimeOffset now)
        : base(id)
    {
        FirstName = LastName = Email = Phone = FiscalCode = PrivacyVersion = string.Empty;
        ClientApplication = clientApplication;
        Language = language;
        Status = RegistrationStatus.Pending;
        RequestedAt = PrivacyConsentedAt = now;
    }

    private RegistrationRequest()
    {
        FirstName = LastName = Email = Phone = FiscalCode = PrivacyVersion = ClientApplication = Language = string.Empty;
    }

    public string FirstName { get; private set; }

    public string LastName { get; private set; }

    /// <summary>Trimmed and lower case (legacy normalisation).</summary>
    public string Email { get; private set; }

    public string Phone { get; private set; }

    public DateOnly BirthDate { get; private set; }

    /// <summary>Upper case, without spaces.</summary>
    public string FiscalCode { get; private set; }

    /// <summary>Language of the e-mails to the applicant.</summary>
    public string Language { get; private set; }

    /// <summary>The privacy notice the applicant accepted, as the client application names it.</summary>
    public string PrivacyVersion { get; private set; }

    public DateTimeOffset PrivacyConsentedAt { get; private set; }

    /// <summary>The <c>client_id</c> of the application that sent it.</summary>
    public string ClientApplication { get; private set; }

    public RegistrationStatus Status { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>The user (Administrator or Employee) who approved or rejected it.</summary>
    public Guid? ProcessedByUserId { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>The client created by the approval.</summary>
    public Guid? ClientId { get; private set; }

    public string FullName => $"{FirstName} {LastName}";

    public PersonDetails ToPersonDetails() => new(FirstName, LastName, Email, BirthDate, Phone, FiscalCode);

    public static Result<RegistrationRequest> Submit(
        Guid id, RegistrationDetails details, string language, string clientApplication, int minimumAge, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientApplication);

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var person = new PersonDetails(details.FirstName, details.LastName, details.Email, details.BirthDate, details.Phone, details.FiscalCode);
        var errors = Person.Validate(person, today);
        Require(errors, "email", details.Email, "validation.person.email");
        Require(errors, "phone", details.Phone, "validation.person.phone");
        Require(errors, "fiscalCode", details.FiscalCode, "validation.person.fiscalCode");
        if (details.BirthDate is null)
        {
            errors.TryAdd("birthDate", ["validation.person.birthDate"]);
        }
        else if (details.BirthDate > today.AddYears(-Math.Max(0, minimumAge)))
        {
            errors.TryAdd("birthDate", ["validation.registration.age"]);
        }

        if (!details.PrivacyConsent)
        {
            errors["privacyConsent"] = ["validation.registration.privacyConsent"];
        }

        var privacyVersion = details.PrivacyVersion?.Trim();
        if (string.IsNullOrEmpty(privacyVersion) || privacyVersion.Length > PrivacyVersionMaxLength)
        {
            errors["privacyVersion"] = ["validation.registration.privacyVersion"];
        }

        if (errors.Count > 0)
        {
            return Errors.Directory.RegistrationInvalid(errors);
        }

        return new RegistrationRequest(id, clientApplication, language, now)
        {
            FirstName = details.FirstName!.Trim(),
            LastName = details.LastName!.Trim(),
            Email = NormalizeEmail(details.Email)!,
            Phone = details.Phone!.Trim(),
            BirthDate = details.BirthDate!.Value,
            FiscalCode = Person.NormalizeFiscalCode(details.FiscalCode)!,
            PrivacyVersion = privacyVersion!,
        };
    }

    /// <summary>Trimmed, lower case; <c>null</c> when empty.</summary>
    /// <summary>
    /// Legacy import (E-05): a registration request as the legacy stored it (its values were checked by the legacy
    /// form, Q54/Q59), with the outcome. Privacy consent is the one the legacy form required (version <c>legacy</c>).
    /// </summary>
    public static RegistrationRequest ImportLegacy(
        Guid id, PersonDetails details, string language, DateTimeOffset requestedAt, RegistrationStatus status, Guid? processedByUserId,
        DateTimeOffset? processedAt, string? notes, Guid? clientId)
    {
        ArgumentNullException.ThrowIfNull(details);

        return new RegistrationRequest(id, "legacy", language, requestedAt)
        {
            FirstName = details.FirstName?.Trim() ?? string.Empty,
            LastName = details.LastName?.Trim() ?? string.Empty,
            Email = NormalizeEmail(details.Email) ?? string.Empty,
            Phone = details.Phone?.Trim() ?? string.Empty,
            BirthDate = details.BirthDate ?? DateOnly.MinValue,
            FiscalCode = Person.NormalizeFiscalCode(details.FiscalCode) ?? string.Empty,
            PrivacyVersion = "legacy",
            Status = status,
            ProcessedByUserId = processedByUserId,
            ProcessedAt = processedAt,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()[..Math.Min(notes.Trim().Length, NotesMaxLength)],
            ClientId = clientId,
        };
    }

    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <summary>Notes too long for <see cref="NotesMaxLength"/> (checked before processing).</summary>
    public static Result CheckNotes(string? notes) =>
        notes?.Trim().Length > NotesMaxLength
            ? Errors.Directory.RegistrationInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["notes"] = ["validation.registration.notes"] })
            : Result.Success();

    /// <summary>The client <paramref name="clientId"/> was created from the request.</summary>
    public Result Approve(Guid clientId, Guid processedByUserId, string? notes, DateTimeOffset now) =>
        Process(RegistrationStatus.Approved, processedByUserId, notes, now, clientId);

    public Result Reject(Guid processedByUserId, string? notes, DateTimeOffset now) =>
        Process(RegistrationStatus.Rejected, processedByUserId, notes, now, null);

    private Result Process(RegistrationStatus status, Guid processedByUserId, string? notes, DateTimeOffset now, Guid? clientId)
    {
        if (Status != RegistrationStatus.Pending)
        {
            return Errors.Directory.RegistrationProcessed();
        }

        if (CheckNotes(notes) is { IsFailure: true } invalid)
        {
            return invalid;
        }

        Status = status;
        ProcessedByUserId = processedByUserId;
        ProcessedAt = now;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ClientId = clientId;
        return Result.Success();
    }

    private static void Require(Dictionary<string, string[]> errors, string field, string? value, string messageKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.TryAdd(field, [messageKey]);
        }
    }
}
