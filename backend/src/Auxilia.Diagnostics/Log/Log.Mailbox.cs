using Microsoft.Extensions.Logging;

namespace Auxilia.Diagnostics;

public static partial class Log
{
    public static partial class Mailbox
    {
        [LoggerMessage(EventId = EventCodes.Mailbox.GrantRevokeFailed, EventName = "Mailbox.GrantRevokeFailed",
            Level = LogLevel.Warning, Message = "The {Provider} grant of connected account {ConnectedAccountId} could not be revoked at the provider")]
        public static partial void GrantRevokeFailed(ILogger logger, Exception? exception, string provider, Guid connectedAccountId);

        [LoggerMessage(EventId = EventCodes.Mailbox.OAuthExchangeFailed, EventName = "Mailbox.OAuthExchangeFailed",
            Level = LogLevel.Warning, Message = "The {Provider} token endpoint did not exchange the code ({Reason})")]
        public static partial void OAuthExchangeFailed(ILogger logger, Exception? exception, string provider, string reason);

        [LoggerMessage(EventId = EventCodes.Mailbox.ImapConnectionFailed, EventName = "Mailbox.ImapConnectionFailed",
            Level = LogLevel.Warning, Message = "IMAP connection test to port {Port} failed ({Reason})")]
        public static partial void ImapConnectionFailed(ILogger logger, Exception? exception, int port, string reason);

        [LoggerMessage(EventId = EventCodes.Mailbox.SyncCredentialsRefused, EventName = "Mailbox.SyncCredentialsRefused",
            Level = LogLevel.Warning, Message = "The {Provider} provider refused the access of connected account {ConnectedAccountId} during a {Scope} sync: it needs to be reconnected")]
        public static partial void SyncCredentialsRefused(ILogger logger, string provider, Guid connectedAccountId, string scope);

        [LoggerMessage(EventId = EventCodes.Mailbox.SyncProviderUnavailable, EventName = "Mailbox.SyncProviderUnavailable",
            Level = LogLevel.Warning, Message = "The {Provider} provider was not reachable during {Scope} sync run {SyncRunId}: the message will be retried")]
        public static partial void SyncProviderUnavailable(ILogger logger, Exception? exception, string provider, string scope, Guid syncRunId);

        [LoggerMessage(EventId = EventCodes.Mailbox.SyncAbandoned, EventName = "Mailbox.SyncAbandoned",
            Level = LogLevel.Warning, Message = "Sync run {SyncRunId} made no progress since {UpdatedAt}: closed as failed")]
        public static partial void SyncAbandoned(ILogger logger, Guid syncRunId, DateTimeOffset updatedAt);

        [LoggerMessage(EventId = EventCodes.Mailbox.SyncCursorReset, EventName = "Mailbox.SyncCursorReset",
            Level = LogLevel.Information, Message = "The {Scope} cursor of connected account {ConnectedAccountId} was reset ({Reason}): full sync of the window")]
        public static partial void SyncCursorReset(ILogger logger, string scope, Guid connectedAccountId, string reason);

        [LoggerMessage(EventId = EventCodes.Mailbox.SyncItemsSkipped, EventName = "Mailbox.SyncItemsSkipped",
            Level = LogLevel.Warning, Message = "{Count} malformed items skipped in {Scope} sync run {SyncRunId}")]
        public static partial void SyncItemsSkipped(ILogger logger, int count, string scope, Guid syncRunId);

        [LoggerMessage(EventId = EventCodes.Mailbox.MailArrived, EventName = "Mailbox.MailArrived",
            Level = LogLevel.Information, Message = "{Count} new messages arrived in connected account {ConnectedAccountId}: the owner was notified")]
        public static partial void MailArrived(ILogger logger, int count, Guid connectedAccountId);
    }
}
