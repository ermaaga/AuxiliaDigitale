namespace Auxilia.Contracts.Identity;

/// <summary>
/// The own profile of the signed-in user (F04): personal data, read-only user name, language, theme (Q35) and the
/// picture. <c>imageVersion</c> is the picture hash (null without a picture): append it to
/// <c>/api/v1/users/{userId}/image?v=</c> so a new picture is never served from a cache.
/// </summary>
public sealed record ProfileResponse(
    Guid UserId,
    string UserName,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    string LanguageCode,
    string Theme,
    string? ImageVersion);

/// <summary>F04: first name, last name, e-mail and phone; the user name cannot be changed here.</summary>
public sealed record UpdateProfileRequest(string FirstName, string LastName, string? Email, string? Phone);

/// <summary>An active language of the tenant (<c>GET /i18n/languages</c>); used for the next sign-ins too.</summary>
public sealed record ChangeLanguageRequest(string LanguageCode);

/// <summary><c>theme</c>: <c>System</c>, <c>Light</c> or <c>Dark</c> (Q35).</summary>
public sealed record UpdatePreferencesRequest(string Theme);

/// <summary>
/// An open session of the signed-in user (F04 "my sessions", Q36: real data from the sessions table); <c>isCurrent</c>
/// marks the session of the request.
/// </summary>
public sealed record MySessionResponse(
    Guid Id,
    string ClientId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    string? IpAddress,
    string? UserAgent,
    bool IsCurrent);
