namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Identity
    {
        public static readonly OperationDescriptor CreateUser = new("Identity.CreateUser", EventCodes.Identity.UserCreated);

        public static readonly OperationDescriptor SetPassword = new("Identity.SetPassword", EventCodes.Identity.PasswordChanged);

        public static readonly OperationDescriptor SetUserActive = new("Identity.SetUserActive", EventCodes.Identity.UserActivationChanged);

        public static readonly OperationDescriptor SetUserRoles = new("Identity.SetUserRoles", EventCodes.Identity.UserRolesChanged);

        /// <summary>Not transactional: failed attempts and lockout must be saved even when the sign-in fails.</summary>
        public static readonly OperationDescriptor AuthenticateUser = new("Identity.AuthenticateUser", EventCodes.Identity.UserAuthenticated, isWrite: false);

        /// <summary>Not transactional as a whole: failed attempts must be saved; the session is saved on success.</summary>
        public static readonly OperationDescriptor SignIn = new("Identity.SignIn", EventCodes.Identity.TokensIssued, isWrite: false);

        /// <summary>Not transactional: a detected reuse must revoke the session even though the refresh fails.</summary>
        public static readonly OperationDescriptor RefreshTokens = new("Identity.RefreshTokens", EventCodes.Identity.TokensRefreshed, isWrite: false);

        public static readonly OperationDescriptor EndSession = new("Identity.EndSession", EventCodes.Identity.SessionEnded);

        public static readonly OperationDescriptor SendActivation = new("Identity.SendActivation", EventCodes.Identity.ActivationSent);

        public static readonly OperationDescriptor ActivateAccount = new("Identity.ActivateAccount", EventCodes.Identity.AccountActivated);

        public static readonly OperationDescriptor RequestPasswordReset = new("Identity.RequestPasswordReset", EventCodes.Identity.PasswordResetRequested);

        public static readonly OperationDescriptor ResetPassword = new("Identity.ResetPassword", EventCodes.Identity.PasswordReset);

        public static readonly OperationDescriptor RotateSigningKey = new("Identity.RotateSigningKey", EventCodes.Identity.SigningKeyRotated);

        public static readonly OperationDescriptor CreatePlatformUser = new("Identity.CreatePlatformUser", EventCodes.Identity.PlatformUserCreated);

        public static readonly OperationDescriptor ResetPlatformCredentials = new("Identity.ResetPlatformCredentials", EventCodes.Identity.PlatformCredentialsReset);

        public static readonly OperationDescriptor SetPlatformUserActive = new("Identity.SetPlatformUserActive", EventCodes.Identity.PlatformUserActivationChanged);

        public static readonly OperationDescriptor BeginPlatformEnrollment = new("Identity.BeginPlatformEnrollment", EventCodes.Identity.PlatformEnrollmentStarted);

        public static readonly OperationDescriptor ActivatePlatformAccount = new("Identity.ActivatePlatformAccount", EventCodes.Identity.PlatformAccountActivated);

        /// <summary>Not transactional: failed attempts and lockout must be saved even when the sign-in fails.</summary>
        public static readonly OperationDescriptor PlatformSignIn = new("Identity.PlatformSignIn", EventCodes.Identity.PlatformTokensIssued, isWrite: false);

        /// <summary>Not transactional: a detected reuse must revoke the session even though the refresh fails.</summary>
        public static readonly OperationDescriptor PlatformRefreshTokens = new("Identity.PlatformRefreshTokens", EventCodes.Identity.PlatformTokensRefreshed, isWrite: false);

        public static readonly OperationDescriptor EndPlatformSession = new("Identity.EndPlatformSession", EventCodes.Identity.PlatformSessionEnded);

        public static readonly OperationDescriptor IssuePlatformTenantToken = new("Identity.IssuePlatformTenantToken", EventCodes.Identity.PlatformTenantTokenIssued, isWrite: false);

        /// <summary>Not transactional: a wrong current password counts toward the lockout and must be saved.</summary>
        public static readonly OperationDescriptor ChangePassword = new("Identity.ChangePassword", EventCodes.Identity.PasswordChangedByUser, isWrite: false);

        public static readonly OperationDescriptor ResetPasswordByOperator = new("Identity.ResetPasswordByOperator", EventCodes.Identity.PasswordResetByOperator);

        public static readonly OperationDescriptor RequestLoginOtp = new("Identity.RequestLoginOtp", EventCodes.Identity.LoginOtpRequested);

        /// <summary>Not transactional: failed attempts, lockout and the attempt log must be saved even when the sign-in fails.</summary>
        public static readonly OperationDescriptor SignInWithOtp = new("Identity.SignInWithOtp", EventCodes.Identity.SignedInWithOtp, isWrite: false);

        /// <summary>Not transactional as a whole: the failed attempt must be saved even when the change is refused.</summary>
        public static readonly OperationDescriptor ChangeExpiredPassword = new("Identity.ChangeExpiredPassword", EventCodes.Identity.ExpiredPasswordChanged, isWrite: false);

        public static readonly OperationDescriptor AddClientApplication = new("Identity.AddClientApplication", EventCodes.Identity.ClientApplicationAdded);
    }
}
