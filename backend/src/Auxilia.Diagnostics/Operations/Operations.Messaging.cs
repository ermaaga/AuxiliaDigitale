namespace Auxilia.Diagnostics;

public static partial class Operations
{
    public static class Messaging
    {
        public static readonly OperationDescriptor CreateAccount = new("Messaging.CreateAccount", EventCodes.Messaging.AccountCreated);

        public static readonly OperationDescriptor UpdateAccount = new("Messaging.UpdateAccount", EventCodes.Messaging.AccountUpdated);

        public static readonly OperationDescriptor SetDefaultAccount = new("Messaging.SetDefaultAccount", EventCodes.Messaging.DefaultAccountChanged);

        public static readonly OperationDescriptor SetAccountActive = new("Messaging.SetAccountActive", EventCodes.Messaging.AccountActivationChanged);

        public static readonly OperationDescriptor SetSenderRules = new("Messaging.SetSenderRules", EventCodes.Messaging.SenderRulesChanged);

        public static readonly OperationDescriptor QueueMessage = new("Messaging.QueueMessage", EventCodes.Messaging.MessageQueued);

        /// <summary>Not one transaction: the send is an external side effect; each state change is saved on its own.</summary>
        public static readonly OperationDescriptor DeliverMessage = new("Messaging.DeliverMessage", EventCodes.Messaging.MessageSent, isWrite: false);

        public static readonly OperationDescriptor SendTestMessage = new("Messaging.SendTestMessage", EventCodes.Messaging.TestMessageSent, isWrite: false);
    }
}
