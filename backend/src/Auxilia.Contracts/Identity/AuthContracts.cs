namespace Auxilia.Contracts.Identity;

/// <summary>
/// <c>POST /auth/token</c>: <c>grantType</c> <c>password</c> (with <c>userName</c> and <c>password</c>),
/// <c>refresh_token</c> (with <c>refreshToken</c>) or <c>email_otp</c> (with <c>userName</c> and the e-mailed <c>code</c>).
/// The client application goes in headers <c>X-Client-Id</c> and, when confidential, <c>X-Client-Secret</c>.
/// </summary>
public sealed record TokenRequest(string GrantType, string? UserName, string? Password, string? RefreshToken, string? Code = null);

/// <summary>Access token (<c>Bearer</c>, <c>expiresIn</c> seconds) and the rotating refresh token that replaces the previous one.</summary>
public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string RefreshToken);

/// <summary><c>POST /auth/activate</c>: the token of the activation link and the first password.</summary>
public sealed record ActivateAccountRequest(string Token, string Password);

/// <summary><c>POST /auth/password/forgot</c>: always 202, whether or not the user exists.</summary>
public sealed record ForgotPasswordRequest(string UserName);

/// <summary><c>POST /auth/password/reset</c>: the token of the reset link and the new password.</summary>
public sealed record ResetPasswordRequest(string Token, string Password);

/// <summary><c>GET /auth/password-policy</c>: the rules a new password must meet (the UI shows them; the API enforces them).</summary>
public sealed record PasswordPolicyResponse(
    int MinLength, bool RequireUppercase, bool RequireLowercase, bool RequireDigit, bool RequireSpecial, int HistoryCount);

/// <summary><c>POST /me/password</c>: change the own password (the current one is required).</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// <c>POST /auth/password/change</c>: an expired password (sign-in answered 403 <c>AUX-12043</c>) is changed with the user
/// name and the current password; the answer is a token pair, as a sign-in.
/// </summary>
public sealed record ChangeExpiredPasswordRequest(string UserName, string CurrentPassword, string NewPassword);

/// <summary><c>POST /auth/otp</c>: e-mail a sign-in code (method <c>email-otp</c>); always 202.</summary>
public sealed record LoginOtpRequest(string UserName);

/// <summary><c>GET /auth/methods</c>: the sign-in methods enabled for the tenant (<c>password</c>, <c>email-otp</c>).</summary>
public sealed record LoginMethodsResponse(IReadOnlyList<string> Methods);

/// <summary>One row of the login audit (<c>GET /identity/login-attempts</c>, F35).</summary>
public sealed record LoginAttemptResponse(
    Guid Id, string UserName, Guid? UserId, string Method, DateTimeOffset AttemptedAt, bool Succeeded, string? FailureReason, string? IpAddress, string? UserAgent);
