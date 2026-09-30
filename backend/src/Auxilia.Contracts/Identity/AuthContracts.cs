namespace Auxilia.Contracts.Identity;

/// <summary>
/// <c>POST /auth/token</c>: <c>grantType</c> <c>password</c> (with <c>userName</c> and <c>password</c>) or
/// <c>refresh_token</c> (with <c>refreshToken</c>). The client application goes in headers <c>X-Client-Id</c> and, when
/// confidential, <c>X-Client-Secret</c>.
/// </summary>
public sealed record TokenRequest(string GrantType, string? UserName, string? Password, string? RefreshToken);

/// <summary>Access token (<c>Bearer</c>, <c>expiresIn</c> seconds) and the rotating refresh token that replaces the previous one.</summary>
public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string RefreshToken);

/// <summary><c>POST /auth/activate</c>: the token of the activation link and the first password.</summary>
public sealed record ActivateAccountRequest(string Token, string Password);

/// <summary><c>POST /auth/password/forgot</c>: always 202, whether or not the user exists.</summary>
public sealed record ForgotPasswordRequest(string UserName);

/// <summary><c>POST /auth/password/reset</c>: the token of the reset link and the new password.</summary>
public sealed record ResetPasswordRequest(string Token, string Password);
