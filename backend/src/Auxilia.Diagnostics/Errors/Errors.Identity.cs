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
    }
}
