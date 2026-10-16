namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Mailbox
    {
        public static readonly OperationDescriptor StartOAuth = new("Mailbox.StartOAuth", EventCodes.Mailbox.OAuthStarted);

        /// <summary>Not one transaction: the request is consumed before the call to the provider, the account saved after it.</summary>
        public static readonly OperationDescriptor CompleteOAuth = new("Mailbox.CompleteOAuth", EventCodes.Mailbox.OAuthAccountConnected, isWrite: false);

        /// <summary>No transaction around the connection test: it runs before the account is opened and saved once.</summary>
        public static readonly OperationDescriptor ConnectImap = new("Mailbox.ConnectImap", EventCodes.Mailbox.ImapAccountConnected, isWrite: false);

        public static readonly OperationDescriptor UpdateAccount = new("Mailbox.UpdateAccount", EventCodes.Mailbox.AccountUpdated);

        /// <summary>The grant is revoked at the provider after the account is deleted (a failure only logs).</summary>
        public static readonly OperationDescriptor DisconnectAccount = new("Mailbox.DisconnectAccount", EventCodes.Mailbox.AccountDisconnected, isWrite: false);

        /// <summary>
        /// Inside the operation of another module (deleting an employee): the accounts go in its transaction, the grants are
        /// revoked at the providers once it has committed.
        /// </summary>
        public static readonly OperationDescriptor RemoveUserAccounts = new("Mailbox.RemoveUserAccounts", EventCodes.Mailbox.UserAccountsRemoved);

        /// <summary>Queues (or reuses) a sync run per account of the owner and scope; the messages leave through the outbox.</summary>
        public static readonly OperationDescriptor RequestSync = new("Mailbox.RequestSync", EventCodes.Mailbox.SyncRequested);

        /// <summary>A sync message of the Worker: no transaction around it, every chunk and state change is its own.</summary>
        public static readonly OperationDescriptor SyncAccount = new("Mailbox.SyncAccount", EventCodes.Mailbox.SyncMessageHandled, isWrite: false);

        /// <summary>One chunk: items and cursor together (an interrupted sync resumes from the last saved chunk).</summary>
        public static readonly OperationDescriptor SaveSyncChunk = new("Mailbox.SaveSyncChunk", EventCodes.Mailbox.SyncChunkSaved);

        public static readonly OperationDescriptor UpdateSyncRun = new("Mailbox.UpdateSyncRun", EventCodes.Mailbox.SyncRunUpdated);

        /// <summary>Not one transaction: the message is read at the provider, the documents are stored by the Documents module, then the filing is recorded.</summary>
        public static readonly OperationDescriptor FileMessage = new("Mailbox.FileMessage", EventCodes.Mailbox.MailFiled, isWrite: false);
    }
}
