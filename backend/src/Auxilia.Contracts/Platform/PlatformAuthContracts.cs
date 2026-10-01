namespace Auxilia.Contracts.Platform;

/// <summary>
/// <c>POST /platform/auth/token</c>: <c>grantType</c> <c>password</c> (with <c>email</c>, <c>password</c> and the
/// authenticator <c>code</c>) or <c>refresh_token</c>. Client application of type <c>PlatformConsole</c> in headers
/// <c>X-Client-Id</c> / <c>X-Client-Secret</c>.
/// </summary>
public sealed record PlatformTokenRequest(string GrantType, string? Email, string? Password, string? Code, string? RefreshToken);

/// <summary><c>POST /platform/auth/enrollment</c>: the activation token printed by <c>auxctl platform users add|reset</c>.</summary>
public sealed record PlatformEnrollmentRequest(string ActivationToken);

/// <summary>The TOTP secret (Base32, for manual entry) and its <c>otpauth://</c> URI (QR code); shown once.</summary>
public sealed record PlatformEnrollmentResponse(string Secret, string Uri);

/// <summary><c>POST /platform/auth/activate</c>: password and a current code of the enrolled authenticator.</summary>
public sealed record PlatformActivateRequest(string ActivationToken, string Password, string Code);

/// <summary>The signed-in platform user (<c>GET /platform/me</c>).</summary>
public sealed record PlatformMeResponse(Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>A tenant in the console list (<c>GET /platform/tenants</c>).</summary>
public sealed record PlatformTenantResponse(string Slug, string DisplayName, string Status, string? SchemaVersion, string? PlanCode);

/// <summary>
/// <c>POST /platform/tenants/{slug}/token</c>: a short-lived platform token for that tenant's technical endpoints
/// (no refresh token: ask again from the console session).
/// </summary>
public sealed record PlatformTenantTokenResponse(string AccessToken, string TokenType, int ExpiresIn, string Tenant);
