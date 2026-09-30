namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(29000, "Security events")]
    public static class Security
    {
        /// <summary>A sign-in failed (unknown user, wrong password, disabled account, no password yet, locked).</summary>
        public const int LoginFailed = 29001;

        /// <summary>An account was locked after too many failed sign-ins.</summary>
        public const int AccountLockedOut = 29002;

        /// <summary>A legacy BCrypt password was verified and rehashed with the current hasher (F01).</summary>
        public const int LegacyPasswordUpgraded = 29003;

        /// <summary>The roles of a user changed.</summary>
        public const int RolesChanged = 29004;

        /// <summary>A user's password was set or changed.</summary>
        public const int PasswordChanged = 29005;

        /// <summary>A user account was enabled or disabled for sign-in.</summary>
        public const int AccountActivationChanged = 29006;
    }
}
