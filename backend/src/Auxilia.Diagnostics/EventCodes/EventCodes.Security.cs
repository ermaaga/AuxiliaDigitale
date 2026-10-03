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

        /// <summary>A platform (System) sign-in failed (wrong password or TOTP code, locked, not enrolled, disabled).</summary>
        public const int PlatformLoginFailed = 29017;

        /// <summary>A platform user was locked out after repeated failures.</summary>
        public const int PlatformAccountLockedOut = 29018;

        /// <summary>A platform user signed in to the console.</summary>
        public const int PlatformSignedIn = 29019;

        /// <summary>A platform user opened a tenant: every technical change in that tenant follows with actor Platform.</summary>
        public const int PlatformTenantAccess = 29020;

        /// <summary>Password and TOTP of a platform user were set or reset.</summary>
        public const int PlatformCredentialsChanged = 29021;

        /// <summary>A one-time sign-in code was e-mailed to a user (F35).</summary>
        public const int LoginOtpSent = 29022;

        /// <summary>An operator reset a user's password with auxctl (F31): temporary password or reset link.</summary>
        public const int PasswordResetByOperator = 29023;

        /// <summary>The permissions of a tenant role changed (F22): permissions granted and revoked.</summary>
        public const int RolePermissionsChanged = 29024;

        /// <summary>The System changed the log level of a tenant (D-28): Debug events may include more detail.</summary>
        public const int TenantLogLevelChanged = 29025;

        /// <summary>A registration request was refused because its captcha is missing, invalid or already used (F02).</summary>
        public const int CaptchaRejected = 29026;

        /// <summary>ALTCHA has no configured key: a random one is used until restart (single node only).</summary>
        public const int CaptchaKeyEphemeral = 29027;

        /// <summary>An upload was refused: type not accepted, content not matching the type, too large (F14).</summary>
        public const int UploadRejected = 29028;

        /// <summary>A storage key outside the current tenant's prefix was refused (tenant isolation).</summary>
        public const int StorageKeyRejected = 29029;
    }
}
