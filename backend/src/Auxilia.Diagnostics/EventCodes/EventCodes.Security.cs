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

        /// <summary>A consumed refresh token was presented again: the session is revoked.</summary>
        public const int RefreshTokenReuse = 29007;

        /// <summary>A session ended (logout, revocation, single session, security stamp).</summary>
        public const int SessionEnded = 29008;

        /// <summary>A password reset was requested.</summary>
        public const int PasswordResetRequested = 29009;

        /// <summary>A password was reset with a reset link.</summary>
        public const int PasswordResetCompleted = 29010;

        /// <summary>An account was activated with its activation link.</summary>
        public const int AccountActivated = 29011;

        /// <summary>A token request came from an unknown or disabled client, or with a wrong secret.</summary>
        public const int ClientRejected = 29012;

        /// <summary>The token signing key was rotated.</summary>
        public const int SigningKeyRotated = 29013;

        /// <summary>A tenant user was denied an operation for a missing permission or by a resource policy.</summary>
        public const int PermissionDenied = 29014;

        /// <summary>A token of one tenant was used against another tenant (the request gets 403 <c>AUX-11004</c>).</summary>
        public const int CrossTenantAttempt = 29015;

        /// <summary>A caller exceeded a rate-limit policy (the request gets 429 <c>AUX-10024</c>).</summary>
        public const int RateLimitExceeded = 29016;
    }
}
