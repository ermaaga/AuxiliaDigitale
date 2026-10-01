namespace Auxilia.Diagnostics;

public static partial class EventCodes
{
    [EventCodeRange(25000, "Messaging (outbound channels, accounts, templates)")]
    public static class Messaging
    {
        /// <summary>A sending account was created.</summary>
        public const int AccountCreated = 25001;

        /// <summary>A sending account was updated (settings, name, secret).</summary>
        public const int AccountUpdated = 25002;

        /// <summary>The default account of a channel changed.</summary>
        public const int DefaultAccountChanged = 25003;

        /// <summary>A sending account was activated or deactivated.</summary>
        public const int AccountActivationChanged = 25004;

        /// <summary>The sender rules of a channel were replaced.</summary>
        public const int SenderRulesChanged = 25005;

        /// <summary>An outbound message was recorded and queued for delivery.</summary>
        public const int MessageQueued = 25006;

        /// <summary>An outbound message was delivered to the channel.</summary>
        public const int MessageSent = 25007;

        /// <summary>An outbound message failed permanently (no more attempts).</summary>
        public const int MessageFailed = 25008;

        /// <summary>A test message was sent through an account.</summary>
        public const int TestMessageSent = 25009;

        /// <summary>No sending account with this id (404).</summary>
        public const int AccountNotFound = 25010;

        /// <summary>No active account resolves for the channel, purpose and role (no rule and no default).</summary>
        public const int NoAccountForMessage = 25011;

        /// <summary>The account settings are not valid for the provider (400).</summary>
        public const int AccountSettingsInvalid = 25012;

        /// <summary>No adapter is installed for the provider of the account (e.g. WhatsApp before its adapter exists).</summary>
        public const int ChannelNotAvailable = 25013;

        /// <summary>No template with this code for the channel in any fallback language.</summary>
        public const int TemplateNotFound = 25014;

        /// <summary>The template cannot be parsed or rendered.</summary>
        public const int TemplateInvalid = 25015;

        /// <summary>The recipient is not valid for the channel or was refused by the server.</summary>
        public const int RecipientInvalid = 25016;

        /// <summary>The default account must stay active; deactivate it after choosing another default (409).</summary>
        public const int DefaultAccountMustBeActive = 25017;

        /// <summary>A delivery attempt failed and will be retried.</summary>
        public const int DeliveryAttemptFailed = 25018;

        /// <summary>No outbound message with this id.</summary>
        public const int OutboundMessageNotFound = 25019;

        /// <summary>A sender rule is not valid (unknown role, account of another channel or inactive).</summary>
        public const int SenderRuleInvalid = 25020;

        /// <summary>The channel server refused the credentials of the account.</summary>
        public const int AccountAuthenticationFailed = 25021;

        /// <summary>A test message could not be delivered: the server did not answer or the connection failed.</summary>
        public const int TestDeliveryFailed = 25022;
    }
}
