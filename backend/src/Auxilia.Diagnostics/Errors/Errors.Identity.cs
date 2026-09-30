using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Identity
    {
        public static Error InvalidCredentials() =>
            Error.Unauthorized(EventCodes.Identity.InvalidCredentials, "Invalid user name or password");

        public static Error AccountLocked() =>
            Error.Forbidden(EventCodes.Identity.AccountLocked, "The account is temporarily locked");

        public static Error UserNameTaken() =>
            Error.Conflict(EventCodes.Identity.UserNameTaken, "The user name is already used");

        public static Error PasswordTooWeak(int minimumLength) =>
            Error.Validation(EventCodes.Identity.PasswordTooWeak, $"The password must have at least {minimumLength} characters",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["password"] = ["validation.password.tooShort"] });

        public static Error UserNotFound() =>
            Error.NotFound(EventCodes.Identity.UserNotFound, "User not found");

        public static Error PersonNotFound() =>
            Error.NotFound(EventCodes.Identity.PersonNotFound, "Person not found");

        public static Error UserValueInvalid(string field) =>
            Error.Validation(EventCodes.Identity.UserValueInvalid, $"The user field {field} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [$"validation.user.{field}"] });

        public static Error RefreshTokenInvalid() =>
            Error.Unauthorized(EventCodes.Identity.RefreshTokenInvalid, "The refresh token is not valid");

        public static Error ClientInvalid() =>
            Error.Unauthorized(EventCodes.Identity.ClientInvalid, "The client application is not valid");

        public static Error UserTokenInvalid() =>
            Error.Validation(EventCodes.Identity.UserTokenInvalid, "The link is not valid or has expired",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["token"] = ["validation.user.linkInvalid"] });

        public static Error UserEmailMissing() =>
            Error.Validation(EventCodes.Identity.UserEmailMissing, "The user has no e-mail address",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["email"] = ["validation.user.emailMissing"] });

        public static Error PermissionDenied() =>
            Error.Forbidden(EventCodes.Identity.PermissionDenied, "The caller is not allowed to perform this operation");

        public static Error ClientIdTaken() =>
            Error.Conflict(EventCodes.Identity.ClientIdTaken, "A client application with this client id already exists");
    }
}
