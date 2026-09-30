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
    }
}
