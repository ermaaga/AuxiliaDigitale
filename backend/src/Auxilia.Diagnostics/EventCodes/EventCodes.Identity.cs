namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(12000, "Identity / Auth")]
    public static class Identity
    {
        /// <summary>A tenant user account was created (no password yet: activation link, D-06).</summary>
        public const int UserCreated = 12001;

        /// <summary>Wrong user name or password, or the account cannot sign in (401; never says which, F01).</summary>
        public const int InvalidCredentials = 12002;

        /// <summary>The account is temporarily locked after too many failed attempts (403).</summary>
        public const int AccountLocked = 12003;

        /// <summary>Another user already has this user name (409).</summary>
        public const int UserNameTaken = 12004;

        /// <summary>The password does not satisfy the password policy (400).</summary>
        public const int PasswordTooWeak = 12005;

        /// <summary>No user with this id (404).</summary>
        public const int UserNotFound = 12006;

        /// <summary>A user's password was set or changed.</summary>
        public const int PasswordChanged = 12007;

        /// <summary>A user account was enabled or disabled for sign-in (D-05).</summary>
        public const int UserActivationChanged = 12008;

        /// <summary>A user's roles changed.</summary>
        public const int UserRolesChanged = 12009;

        /// <summary>A user signed in with user name and password.</summary>
        public const int UserAuthenticated = 12010;

        /// <summary>The person of the account does not exist (404).</summary>
        public const int PersonNotFound = 12011;

        /// <summary>A user value is not valid (user name, e-mail, language, roles) (400).</summary>
        public const int UserValueInvalid = 12012;
    }
}
