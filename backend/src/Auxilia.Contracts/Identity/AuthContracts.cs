namespace Auxilia.Contracts.Identity;

/// <summary>
/// <c>POST /auth/token</c>: <c>grantType</c> <c>password</c> (with <c>userName</c> and <c>password</c>),
/// <c>refresh_token</c> (with <c>refreshToken</c>) or <c>email_otp</c> (with <c>userName</c> and the e-mailed <c>code</c>).
/// The client application goes in headers <c>X-Client-Id</c> and, when confidential, <c>X-Client-Secret</c>.
/// <c>twoFactorCode</c> is the code of the authenticator app, asked (401 <c>AUX-12072</c>) when the user has it (N04);
/// <c>rememberMe</c> is "stay signed in".
/// </summary>
public sealed record TokenRequest(
    string GrantType, string? UserName, string? Password, string? RefreshToken, string? Code = null, string? TwoFactorCode = null, bool RememberMe = false);

/// <summary>
/// Access token (<c>Bearer</c>, <c>expiresIn</c> seconds) and the rotating refresh token that replaces the previous one.
/// <c>sessionExpiresIn</c> (seconds) only for a "stay signed in" session: how long it stays open without activity, so a
/// web app keeps its cookie as long (N04).
/// </summary>
public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string RefreshToken, int? SessionExpiresIn = null);

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
public sealed record ChangeExpiredPasswordRequest(
    string UserName, string CurrentPassword, string NewPassword, string? TwoFactorCode = null, bool RememberMe = false);

/// <summary><c>POST /auth/otp</c>: e-mail a sign-in code (method <c>email-otp</c>); always 202.</summary>
public sealed record LoginOtpRequest(string UserName);

/// <summary>
/// <c>GET /auth/methods</c>: the sign-in methods enabled for the tenant (<c>password</c>, <c>email-otp</c>) and how many
/// days "stay signed in" lasts (0 = the box is hidden, N04).
/// </summary>
public sealed record LoginMethodsResponse(IReadOnlyList<string> Methods, int RememberMeDays = 0);

/// <summary>
/// <c>POST /auth/two-factor/setup</c>: the user's roles require the authenticator app and none is set (sign-in answered
/// 403 <c>AUX-12074</c>): user name and password start the enrolment (N04).
/// </summary>
public sealed record TwoFactorSetupRequest(string UserName, string Password);

/// <summary><c>POST /auth/two-factor/setup/confirm</c>: the code of the app confirms the enrolment; the answer is a token pair.</summary>
public sealed record TwoFactorSetupConfirmRequest(string UserName, string Password, string Code, bool RememberMe = false);

/// <summary>A new secret of the authenticator app: the setup key and the <c>otpauth://</c> URI of the QR code (shown once).</summary>
public sealed record TwoFactorEnrollmentResponse(string Secret, string Uri);

/// <summary><c>GET /me/two-factor</c>: whether the app is set and whether the user's roles require it.</summary>
public sealed record TwoFactorStatusResponse(bool Enabled, bool Required, DateTimeOffset? EnabledAt);

/// <summary><c>POST /me/two-factor/confirm</c>: a code of the app enrolled with <c>POST /me/two-factor/enrollment</c>.</summary>
public sealed record ConfirmTwoFactorRequest(string Code);

/// <summary><c>POST /me/two-factor/disable</c>: the current password is required.</summary>
public sealed record DisableTwoFactorRequest(string Password);

/// <summary>One row of the login audit (<c>GET /identity/login-attempts</c>, F35).</summary>
public sealed record LoginAttemptResponse(
    Guid Id, string UserName, Guid? UserId, string Method, DateTimeOffset AttemptedAt, bool Succeeded, string? FailureReason, string? IpAddress, string? UserAgent);
