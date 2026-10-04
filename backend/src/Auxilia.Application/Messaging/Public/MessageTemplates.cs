namespace Auxilia.Application.Messaging.Public;

/// <summary>Codes of the system templates (seeded in EN and IT, N03).</summary>
public static class MessageTemplates
{
    public const string AccountActivation = "account-activation";
    public const string PasswordReset = "password-reset";
    public const string LoginOtp = "login-otp";
    public const string RegistrationReceived = "registration-received";
    public const string CaseExpiryReminder = "case-expiry-reminder";
    public const string RequestReply = "request-reply";
    public const string AccountTest = "account-test";

    /// <summary>A notification by e-mail (F16): <c>title</c>, <c>message</c> (already in the recipient's language) and <c>link</c>.</summary>
    public const string Notification = "notification";

    /// <summary>Language of last resort when neither the recipient's nor the tenant's language has the template.</summary>
    public const string FallbackLanguage = "en";
}
