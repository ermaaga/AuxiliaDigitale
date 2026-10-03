namespace Auxilia.Contracts.Directory;

/// <summary>
/// A registration request sent by an external client application (F02, API only D-14). Every personal field is
/// required; <c>privacyConsent</c> must be true and <c>privacyVersion</c> names the notice accepted (stored, Q08).
/// <c>language</c> is the language of the e-mails (an active tenant language, else <c>registration.defaultLanguage</c>).
/// <c>captcha</c> is the solution of the challenge from <c>GET /registrations/captcha</c> (ALTCHA payload, base64)
/// when the client application needs one.
/// </summary>
public sealed record SubmitRegistrationRequest(
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    DateOnly? BirthDate,
    string FiscalCode,
    bool PrivacyConsent,
    string PrivacyVersion,
    string? Language,
    string? Captcha);

/// <summary>The request is waiting for staff review.</summary>
public sealed record RegistrationSubmittedResponse(Guid Id);

/// <summary>An ALTCHA challenge (altcha.org widget or solver format).</summary>
public sealed record AltchaChallengeResponse(string Algorithm, string Challenge, string Salt, string Signature, long Maxnumber);

/// <summary>The captcha the calling client application needs: <c>provider</c> <c>none</c> (no challenge) or <c>altcha</c>.</summary>
public sealed record CaptchaChallengeResponse(string Provider, AltchaChallengeResponse? Altcha);

/// <summary>The staff user who approved or rejected a request.</summary>
public sealed record RegistrationProcessorResponse(Guid UserId, string FullName);

/// <summary>
/// A registration request (F03): <c>status</c> <c>Pending</c>, <c>Approved</c> or <c>Rejected</c> (Q07); after an
/// approval <c>clientId</c> is the client created.
/// </summary>
public sealed record RegistrationResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    DateOnly BirthDate,
    string FiscalCode,
    string Language,
    string PrivacyVersion,
    DateTimeOffset PrivacyConsentedAt,
    string ClientApplication,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ProcessedAt,
    RegistrationProcessorResponse? ProcessedBy,
    string? Notes,
    Guid? ClientId);

/// <summary>Optional notes (at most 500 characters) on an approval or a rejection.</summary>
public sealed record ProcessRegistrationRequest(string? Notes);

/// <summary>
/// The client created by an approval (F03): username = e-mail, assigned to the default employee (Q31). With an
/// employee the client can sign in and gets the activation e-mail (D-06); <c>invitationSent</c> tells whether it left
/// (otherwise <c>invitationErrorCode</c>, e.g. no default employee).
/// </summary>
public sealed record ApproveRegistrationResponse(Guid ClientId, bool InvitationSent, string? InvitationErrorCode);

/// <summary>
/// The registration requests (F03): <c>status</c> filter (<c>Pending</c>, <c>Approved</c>, <c>Rejected</c>; all when
/// empty), request date range <c>from</c>–<c>to</c> (inclusive days, UTC), <c>search</c> in name, surname, e-mail and
/// fiscal code; <c>sort</c> one of <c>requestedAt</c> (default), <c>processedAt</c>, <c>lastName</c>, <c>-</c> for
/// descending.
/// </summary>
public sealed record RegistrationListQuery(
    string? Status,
    DateOnly? From,
    DateOnly? To,
    string? Search,
    string? Sort,
    int Page,
    int PageSize);
