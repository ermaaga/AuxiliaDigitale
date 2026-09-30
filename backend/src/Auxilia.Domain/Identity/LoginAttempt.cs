namespace Auxilia.Domain.Identity;

/// <summary>
/// One sign-in attempt (<c>identity.login_attempts</c>, F35): every method, success or failure, with the user name as
/// typed (the user is known only when it exists) and a coarse failure reason (the detailed one is in the security log).
/// </summary>
public sealed class LoginAttempt
{
    public const int UserNameMaxLength = 256;
    public const int MethodMaxLength = 30;
    public const int ReasonMaxLength = 50;

    public LoginAttempt(
        Guid id, Guid? userId, string userName, string method, DateTimeOffset attemptedAt, bool succeeded, string? failureReason, string? ipAddress, string? userAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        Id = id;
        UserId = userId;
        UserName = Truncate(userName?.Trim(), UserNameMaxLength) ?? string.Empty;
        Method = method;
        AttemptedAt = attemptedAt;
        Succeeded = succeeded;
        FailureReason = succeeded ? null : Truncate(failureReason, ReasonMaxLength);
        IpAddress = Truncate(ipAddress, RefreshSession.IpMaxLength);
        UserAgent = Truncate(userAgent, RefreshSession.UserAgentMaxLength);
    }

    private LoginAttempt()
    {
        UserName = Method = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid? UserId { get; private set; }

    public string UserName { get; private set; }

    /// <summary><c>password</c>, <c>email-otp</c> (future: external providers).</summary>
    public string Method { get; private set; }

    public DateTimeOffset AttemptedAt { get; private set; }

    public bool Succeeded { get; private set; }

    /// <summary><c>InvalidCredentials</c>, <c>InvalidOtp</c>, <c>LockedOut</c>, <c>PasswordExpired</c>, <c>ClientInvalid</c>.</summary>
    public string? FailureReason { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    private static string? Truncate(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length > length ? value[..length] : value;
}
