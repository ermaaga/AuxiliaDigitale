using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Mailbox
    {
        public static Error AccountNotFound() =>
            Error.NotFound(EventCodes.Mailbox.AccountNotFound, "Connected account not found");

        public static Error ProviderNotAvailable(string provider) =>
            Error.NotFound(EventCodes.Mailbox.ProviderNotAvailable, $"Provider {provider} is not available");

        public static Error AccountInvalid(IReadOnlyDictionary<string, string[]> errors) =>
            Error.Validation(EventCodes.Mailbox.AccountInvalid, "The connected account is not valid", errors);

        public static Error OAuthStateInvalid() =>
            Error.Validation(EventCodes.Mailbox.OAuthStateInvalid, "The OAuth answer does not match a pending request");

        public static Error OAuthDenied() =>
            Error.Validation(EventCodes.Mailbox.OAuthDenied, "Access was not granted at the provider");

        public static Error OAuthExchangeFailed() =>
            Error.Failure(EventCodes.Mailbox.OAuthExchangeFailed, "The provider did not complete the authorization");

        public static Error ImapHostRefused() =>
            Error.Validation(EventCodes.Mailbox.ImapHostRefused, "The IMAP server is not allowed",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["host"] = ["validation.mailbox.hostRefused"] });

        public static Error ImapAuthenticationFailed() =>
            Error.Validation(EventCodes.Mailbox.ImapAuthenticationFailed, "The IMAP server refused the credentials",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["password"] = ["validation.mailbox.credentials"] });

        public static Error ImapConnectionFailed() =>
            Error.Failure(EventCodes.Mailbox.ImapConnectionFailed, "The IMAP server could not be reached securely");

        public static Error SyncCredentialsRefused() =>
            Error.Failure(EventCodes.Mailbox.SyncCredentialsRefused, "The provider refused the stored access: reconnect the account");

        public static Error SyncProviderUnavailable() =>
            Error.Failure(EventCodes.Mailbox.SyncProviderUnavailable, "The provider could not be reached");

        public static Error SyncAbandoned() =>
            Error.Failure(EventCodes.Mailbox.SyncAbandoned, "The synchronisation stopped without finishing");

        public static Error SyncScopeInvalid() =>
            Error.Validation(EventCodes.Mailbox.SyncScopeInvalid, "The sync scope is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["scope"] = ["validation.mailbox.scope"] });

        public static Error CalendarRangeInvalid() =>
            Error.Validation(EventCodes.Mailbox.CalendarRangeInvalid, "The calendar range is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["to"] = ["validation.mailbox.range"] });
    }
}
