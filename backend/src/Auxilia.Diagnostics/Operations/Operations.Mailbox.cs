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
    }
}
