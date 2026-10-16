namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(30000, "Mailbox: connected accounts, mail, external calendars (N05)")]
    public static class Mailbox
    {
        /// <summary>A user started connecting a Google or Microsoft account: the provider's consent page was prepared.</summary>
        public const int OAuthStarted = 30001;

        /// <summary>A Google or Microsoft account was connected (or reconnected) after the provider's consent.</summary>
        public const int OAuthAccountConnected = 30002;

        /// <summary>A PEC / IMAP account was connected (or its password replaced) after a successful connection test.</summary>
        public const int ImapAccountConnected = 30003;

        /// <summary>A connected account was changed by its owner (colour).</summary>
        public const int AccountUpdated = 30004;

        /// <summary>A connected account was disconnected: secret and copied data deleted.</summary>
        public const int AccountDisconnected = 30005;

        /// <summary>No connected account of the caller with this id (404; other users' accounts are not visible).</summary>
        public const int AccountNotFound = 30006;

        /// <summary>The provider is not offered: unknown, or its OAuth application is not configured.</summary>
        public const int ProviderNotAvailable = 30007;

        /// <summary>The request to connect or change an account is not valid (fields in the response).</summary>
        public const int AccountInvalid = 30008;

        /// <summary>The OAuth answer does not match a pending request of the caller (unknown, expired or used state).</summary>
        public const int OAuthStateInvalid = 30009;

        /// <summary>The user did not grant access at the provider (consent refused or cancelled).</summary>
        public const int OAuthDenied = 30010;

        /// <summary>The provider did not exchange the authorization code or its answer was not usable.</summary>
        public const int OAuthExchangeFailed = 30011;

        /// <summary>The IMAP server address is not allowed (private, loopback or metadata address, port not allowed).</summary>
        public const int ImapHostRefused = 30012;

        /// <summary>The IMAP server refused the user name or password.</summary>
        public const int ImapAuthenticationFailed = 30013;

        /// <summary>The IMAP server could not be reached or the secure connection failed.</summary>
        public const int ImapConnectionFailed = 30014;

        /// <summary>The grant could not be revoked at the provider while disconnecting; the account is disconnected anyway.</summary>
        public const int GrantRevokeFailed = 30015;

        /// <summary>The owner asked to synchronise mail or calendar (page opened, "Sync now"): runs queued or reused.</summary>
        public const int SyncRequested = 30016;

        /// <summary>A sync message of the Worker was handled (the run went on, continued, ended or failed).</summary>
        public const int SyncMessageHandled = 30017;

        /// <summary>A chunk of a sync was saved with its cursor (one transaction).</summary>
        public const int SyncChunkSaved = 30018;

        /// <summary>A sync run changed state (started, continued, succeeded, failed, abandoned).</summary>
        public const int SyncRunUpdated = 30019;

        /// <summary>The provider refused the stored grant or password during a sync: the account needs to be reconnected.</summary>
        public const int SyncCredentialsRefused = 30020;

        /// <summary>The provider could not be reached or asked to slow down during a sync: the bus retries it.</summary>
        public const int SyncProviderUnavailable = 30021;

        /// <summary>A sync made no progress for too long (its message failed every retry or the Worker stopped): closed as failed.</summary>
        public const int SyncAbandoned = 30022;

        /// <summary>The sync scope is not <c>mail</c> or <c>calendar</c>.</summary>
        public const int SyncScopeInvalid = 30023;

        /// <summary>The calendar range is not valid (from after to, or longer than allowed).</summary>
        public const int CalendarRangeInvalid = 30024;

        /// <summary>The provider no longer accepted the sync token (or the window moved): a bounded full sync starts again.</summary>
        public const int SyncCursorReset = 30025;

        /// <summary>Malformed items of a chunk were skipped and counted; the chunk went on.</summary>
        public const int SyncItemsSkipped = 30026;
    }
}
