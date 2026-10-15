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
    }
}
