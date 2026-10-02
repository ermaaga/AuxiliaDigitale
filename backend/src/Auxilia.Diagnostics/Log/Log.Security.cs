using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    /// <summary>Security events (skill auxilia-security): always logged, never with passwords or user names.</summary>
    public static partial class Security
    {
        [LoggerMessage(EventId = EventCodes.Security.LoginFailed, EventName = "Security.LoginFailed",
            Level = LogLevel.Warning, Message = "Sign-in failed ({Reason}) for user {UserId}")]
        public static partial void LoginFailed(ILogger logger, string reason, Guid? userId);

        [LoggerMessage(EventId = EventCodes.Security.AccountLockedOut, EventName = "Security.AccountLockedOut",
            Level = LogLevel.Warning, Message = "User {UserId} locked out until {LockoutEnd} after {FailedAttempts} failed sign-ins")]
        public static partial void AccountLockedOut(ILogger logger, Guid userId, DateTimeOffset lockoutEnd, int failedAttempts);

        [LoggerMessage(EventId = EventCodes.Security.LegacyPasswordUpgraded, EventName = "Security.LegacyPasswordUpgraded",
            Level = LogLevel.Information, Message = "Legacy password of user {UserId} rehashed")]
        public static partial void LegacyPasswordUpgraded(ILogger logger, Guid userId);

        [LoggerMessage(EventId = EventCodes.Security.RolesChanged, EventName = "Security.RolesChanged",
            Level = LogLevel.Information, Message = "Roles of user {UserId} changed to {Roles}")]
        public static partial void RolesChanged(ILogger logger, Guid userId, IReadOnlyCollection<SharedKernel.Tenancy.TenantRole> roles);

        [LoggerMessage(EventId = EventCodes.Security.PasswordChanged, EventName = "Security.PasswordChanged",
            Level = LogLevel.Information, Message = "Password of user {UserId} changed")]
        public static partial void PasswordChanged(ILogger logger, Guid userId);

        [LoggerMessage(EventId = EventCodes.Security.AccountActivationChanged, EventName = "Security.AccountActivationChanged",
            Level = LogLevel.Information, Message = "User {UserId} can sign in: {IsActive}")]
        public static partial void AccountActivationChanged(ILogger logger, Guid userId, bool isActive);

        [LoggerMessage(EventId = EventCodes.Security.RefreshTokenReuse, EventName = "Security.RefreshTokenReuse",
            Level = LogLevel.Warning, Message = "Refresh token reused in session {SessionId} of user {UserId}: session revoked")]
        public static partial void RefreshTokenReuse(ILogger logger, Guid sessionId, Guid userId);

        [LoggerMessage(EventId = EventCodes.Security.SessionEnded, EventName = "Security.SessionEnded",
            Level = LogLevel.Information, Message = "Session {SessionId} of user {UserId} ended: {Reason}")]
        public static partial void SessionEnded(ILogger logger, Guid sessionId, Guid userId, string reason);

        [LoggerMessage(EventId = EventCodes.Security.PasswordResetRequested, EventName = "Security.PasswordResetRequested",
            Level = LogLevel.Information, Message = "Password reset requested for user {UserId}")]
        public static partial void PasswordResetRequested(ILogger logger, Guid? userId);

        [LoggerMessage(EventId = EventCodes.Security.PasswordResetCompleted, EventName = "Security.PasswordResetCompleted",
            Level = LogLevel.Information, Message = "Password of user {UserId} reset with a reset link")]
        public static partial void PasswordResetCompleted(ILogger logger, Guid userId);

        [LoggerMessage(EventId = EventCodes.Security.AccountActivated, EventName = "Security.AccountActivated",
            Level = LogLevel.Information, Message = "User {UserId} activated the account")]
        public static partial void AccountActivated(ILogger logger, Guid userId);

        [LoggerMessage(EventId = EventCodes.Security.ClientRejected, EventName = "Security.ClientRejected",
            Level = LogLevel.Warning, Message = "Token request rejected for client {ClientId}: {Reason}")]
        public static partial void ClientRejected(ILogger logger, string clientId, string reason);

        [LoggerMessage(EventId = EventCodes.Security.SigningKeyRotated, EventName = "Security.SigningKeyRotated",
            Level = LogLevel.Information, Message = "Token signing key {KeyId} is now active")]
        public static partial void SigningKeyRotated(ILogger logger, string keyId);

        [LoggerMessage(EventId = EventCodes.Security.PermissionDenied, EventName = "Security.PermissionDenied",
            Level = LogLevel.Warning, Message = "User {UserId} denied {Permission} ({Reason})")]
        public static partial void PermissionDenied(ILogger logger, Guid? userId, string permission, string reason);

        [LoggerMessage(EventId = EventCodes.Security.CrossTenantAttempt, EventName = "Security.CrossTenantAttempt",
            Level = LogLevel.Warning, Message = "Cross-tenant attempt: claim tenant {ClaimTenant}, requested tenant {RequestedTenant}")]
        public static partial void CrossTenantAttempt(ILogger logger, string claimTenant, string requestedTenant);

        [LoggerMessage(EventId = EventCodes.Security.RateLimitExceeded, EventName = "Security.RateLimitExceeded",
            Level = LogLevel.Warning, Message = "Rate limit {Policy} exceeded by {PartitionKind} on {RequestMethod} {RequestPath}")]
        public static partial void RateLimitExceeded(ILogger logger, string policy, string partitionKind, string requestMethod, string requestPath);

        [LoggerMessage(EventId = EventCodes.Security.PlatformLoginFailed, EventName = "Security.PlatformLoginFailed",
            Level = LogLevel.Warning, Message = "Platform sign-in failed ({Reason}) for platform user {PlatformUserId}")]
        public static partial void PlatformLoginFailed(ILogger logger, string reason, Guid? platformUserId);

        [LoggerMessage(EventId = EventCodes.Security.PlatformAccountLockedOut, EventName = "Security.PlatformAccountLockedOut",
            Level = LogLevel.Warning, Message = "Platform user {PlatformUserId} locked out until {LockoutEnd}")]
        public static partial void PlatformAccountLockedOut(ILogger logger, Guid platformUserId, DateTimeOffset lockoutEnd);

        [LoggerMessage(EventId = EventCodes.Security.PlatformSignedIn, EventName = "Security.PlatformSignedIn",
            Level = LogLevel.Information, Message = "Platform user {PlatformUserId} signed in (session {SessionId})")]
        public static partial void PlatformSignedIn(ILogger logger, Guid platformUserId, Guid sessionId);

        [LoggerMessage(EventId = EventCodes.Security.PlatformTenantAccess, EventName = "Security.PlatformTenantAccess",
            Level = LogLevel.Information, Message = "Platform user {PlatformUserId} opened tenant {TenantSlug}")]
        public static partial void PlatformTenantAccess(ILogger logger, Guid platformUserId, string tenantSlug);

        [LoggerMessage(EventId = EventCodes.Security.PlatformCredentialsChanged, EventName = "Security.PlatformCredentialsChanged",
            Level = LogLevel.Information, Message = "Credentials of platform user {PlatformUserId} {Change}")]
        public static partial void PlatformCredentialsChanged(ILogger logger, Guid platformUserId, string change);

        [LoggerMessage(EventId = EventCodes.Security.LoginOtpSent, EventName = "Security.LoginOtpSent",
            Level = LogLevel.Information, Message = "Sign-in code e-mailed to user {UserId}")]
        public static partial void LoginOtpSent(ILogger logger, Guid userId);

        [LoggerMessage(EventId = EventCodes.Security.PasswordResetByOperator, EventName = "Security.PasswordResetByOperator",
            Level = LogLevel.Warning, Message = "Password of user {UserId} reset by an operator ({Mode})")]
        public static partial void PasswordResetByOperator(ILogger logger, Guid userId, string mode);

        [LoggerMessage(EventId = EventCodes.Security.RolePermissionsChanged, EventName = "Security.RolePermissionsChanged",
            Level = LogLevel.Warning, Message = "Permissions of role {Role} changed: granted {Granted}, revoked {Revoked}")]
        public static partial void RolePermissionsChanged(ILogger logger, string role, string granted, string revoked);

        [LoggerMessage(EventId = EventCodes.Security.TenantLogLevelChanged, EventName = "Security.TenantLogLevelChanged",
            Level = LogLevel.Warning, Message = "Debug logging of tenant {TenantSlug} set until {DebugUntil} (empty: disabled)")]
        public static partial void TenantLogLevelChanged(ILogger logger, string tenantSlug, DateTimeOffset? debugUntil);
    }
}
