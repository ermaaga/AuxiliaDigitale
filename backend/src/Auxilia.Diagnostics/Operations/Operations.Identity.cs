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
    }
}
