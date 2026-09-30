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

        /// <summary>A user signed in on a client application: access and refresh tokens issued.</summary>
        public const int TokensIssued = 12013;

        /// <summary>A refresh token was rotated and a new access token issued.</summary>
        public const int TokensRefreshed = 12014;

        /// <summary>The refresh token is unknown, expired, reused or belongs to an ended session (401).</summary>
        public const int RefreshTokenInvalid = 12015;

        /// <summary>The client application is unknown, disabled or its secret is wrong (401).</summary>
        public const int ClientInvalid = 12016;

        /// <summary>A session ended (logout or revocation).</summary>
        public const int SessionEnded = 12017;

        /// <summary>The activation or password-reset link is invalid, used or expired (400).</summary>
        public const int UserTokenInvalid = 12018;

        /// <summary>An activation link was sent to a user (D-06).</summary>
        public const int ActivationSent = 12019;

        /// <summary>A user activated the account and chose a password.</summary>
        public const int AccountActivated = 12020;

        /// <summary>A password reset was requested (the answer never tells whether the user exists).</summary>
        public const int PasswordResetRequested = 12021;

        /// <summary>A password was reset with a reset link.</summary>
        public const int PasswordReset = 12022;

        /// <summary>The access token was revoked (logout, ended session) (401).</summary>
        public const int AccessTokenRevoked = 12023;

        /// <summary>A new token signing key became active (the previous one stays published for validation).</summary>
        public const int SigningKeyRotated = 12024;

        /// <summary>The user has no e-mail address to send the link to (400).</summary>
        public const int UserEmailMissing = 12025;

        /// <summary>A client application was registered (<c>auxctl clients add</c>).</summary>
        public const int ClientApplicationAdded = 12026;

        /// <summary>A client application with the same client id already exists (409).</summary>
        public const int ClientIdTaken = 12027;

        /// <summary>The caller lacks the permission, or a resource policy denies the resource (403).</summary>
        public const int PermissionDenied = 12028;

        /// <summary>The tenant's permissions were aligned with the module descriptors (new ones granted to their default roles).</summary>
        public const int PermissionsSynchronized = 12029;

        /// <summary>A platform (System) user was created (<c>auxctl platform users add</c>).</summary>
        public const int PlatformUserCreated = 12030;

        /// <summary>The credentials of a platform user were reset: a new activation token was issued.</summary>
        public const int PlatformCredentialsReset = 12031;

        /// <summary>A platform user was enabled or disabled.</summary>
        public const int PlatformUserActivationChanged = 12032;

        /// <summary>TOTP enrolment of a platform user started (secret shown once).</summary>
        public const int PlatformEnrollmentStarted = 12033;

        /// <summary>A platform user set password and TOTP with the activation token.</summary>
        public const int PlatformAccountActivated = 12034;

        /// <summary>A platform user signed in to the console (password + TOTP).</summary>
        public const int PlatformTokensIssued = 12035;

        /// <summary>A console session refreshed its tokens.</summary>
        public const int PlatformTokensRefreshed = 12036;

        /// <summary>A console session ended.</summary>
        public const int PlatformSessionEnded = 12037;

        /// <summary>A platform user opened a tenant (tenant-scoped platform token).</summary>
        public const int PlatformTenantTokenIssued = 12038;

        /// <summary>A platform user with the same e-mail already exists (409).</summary>
        public const int PlatformUserEmailTaken = 12039;

        /// <summary>The endpoint is for platform (System) users only, or needs a tenant-scoped platform token (403).</summary>
        public const int PlatformAccessRequired = 12040;

        /// <summary>The TOTP code is not valid for the enrolment (400).</summary>
        public const int TwoFactorCodeInvalid = 12041;

        /// <summary>The new password matches one of the last N passwords (400, F35).</summary>
        public const int PasswordReused = 12042;

        /// <summary>The password expired: it must be changed before signing in (403, F35).</summary>
        public const int PasswordExpired = 12043;

        /// <summary>The current password given to change it is wrong (400).</summary>
        public const int CurrentPasswordInvalid = 12044;

        /// <summary>The sign-in method is not enabled for the tenant (400).</summary>
        public const int LoginMethodDisabled = 12045;

        /// <summary>A sign-in code was requested by e-mail (method <c>email-otp</c>).</summary>
        public const int LoginOtpRequested = 12046;

        /// <summary>A user changed their own password.</summary>
        public const int PasswordChangedByUser = 12047;

        /// <summary>Tokens were issued for a sign-in with an e-mailed code.</summary>
        public const int SignedInWithOtp = 12048;

        /// <summary>An expired password was changed and the user signed in.</summary>
        public const int ExpiredPasswordChanged = 12049;
    }
}
