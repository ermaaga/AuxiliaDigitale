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

        /// <param name="rules">Translation keys of the rules the password breaks (<c>validation.password.*</c>).</param>
        public static Error PasswordPolicyViolated(IReadOnlyList<string> rules) =>
            Error.Validation(EventCodes.Identity.PasswordTooWeak, "The password does not meet the password policy",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["password"] = [.. rules ?? []] });

        public static Error PasswordReused(int historyCount) =>
            Error.Validation(EventCodes.Identity.PasswordReused, $"The password was used among the last {historyCount}",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["password"] = ["validation.password.reused"] });

        public static Error PasswordExpired() =>
            Error.Forbidden(EventCodes.Identity.PasswordExpired, "The password expired and must be changed");

        public static Error CurrentPasswordInvalid() =>
            Error.Validation(EventCodes.Identity.CurrentPasswordInvalid, "The current password is not correct",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["currentPassword"] = ["validation.password.currentInvalid"] });

        public static Error LoginMethodDisabled(string method) =>
            Error.Validation(EventCodes.Identity.LoginMethodDisabled, $"The sign-in method {method} is not enabled",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["grantType"] = ["validation.auth.methodDisabled"] });

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

        public static Error PlatformUserEmailTaken() =>
            Error.Conflict(EventCodes.Identity.PlatformUserEmailTaken, "A platform user with this e-mail already exists");

        public static Error PlatformAccessRequired() =>
            Error.Forbidden(EventCodes.Identity.PlatformAccessRequired, "The endpoint requires a platform (System) token of the right kind");

        public static Error TenantUserRequired() =>
            Error.Forbidden(EventCodes.Identity.TenantUserRequired, "The endpoint requires the token of a tenant user");

        public static Error TwoFactorCodeInvalid() =>
            Error.Validation(EventCodes.Identity.TwoFactorCodeInvalid, "The authenticator code is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["code"] = ["validation.auth.totpInvalid"] });

        public static Error UserAmbiguous() =>
            Error.Conflict(EventCodes.Identity.UserAmbiguous, "Several users have this e-mail address: use the user name");

        public static Error AdministratorAlreadyExists() =>
            Error.Conflict(EventCodes.Identity.AdministratorAlreadyExists, "The tenant already has an Administrator");

        public static Error AccountAlreadyActivated() =>
            Error.Conflict(EventCodes.Identity.AccountAlreadyActivated, "The account is already activated");

        public static Error ClientIdTaken() =>
            Error.Conflict(EventCodes.Identity.ClientIdTaken, "A client application with this client id already exists");

        public static Error RolePermissionsInvalid(string field, string messageKey) =>
            Error.Validation(EventCodes.Identity.RolePermissionsInvalid, $"The role permission change is not valid ({field})",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageKey] });

        public static Error LanguageNotAvailable() =>
            Error.Validation(EventCodes.Identity.LanguageNotAvailable, "The language is not an active language of the tenant",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["languageCode"] = ["validation.profile.language"] });

        public static Error ProfileImageInvalid() =>
            Error.Validation(EventCodes.Identity.ProfileImageInvalid, "The profile picture must be a JPEG, PNG or WebP image of at most 2 MB",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["file"] = ["validation.profile.image"] });

        public static Error ProfileImageNotFound() =>
            Error.NotFound(EventCodes.Identity.ProfileImageNotFound, "The user has no profile picture");

        public static Error SessionNotFound() =>
            Error.NotFound(EventCodes.Identity.SessionNotFound, "No open session with this id");
    }
}
