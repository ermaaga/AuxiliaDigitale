using Auxilia.SharedKernel.Results;

namespace Auxilia.Diagnostics;

public static partial class Errors
{
    public static class Messaging
    {
        public static Error AccountNotFound() =>
            Error.NotFound(EventCodes.Messaging.AccountNotFound, "Sending account not found");

        public static Error NoAccountForMessage(string channel, string purpose) =>
            Error.Failure(EventCodes.Messaging.NoAccountForMessage, $"No active {channel} account for purpose {purpose}");

        public static Error AccountSettingsInvalid(string field) =>
            Error.Validation(EventCodes.Messaging.AccountSettingsInvalid, $"The account field {field} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = ["validation.messaging.accountInvalid"] });

        public static Error ChannelNotAvailable(string provider) =>
            Error.Failure(EventCodes.Messaging.ChannelNotAvailable, $"No channel adapter for provider {provider}");

        public static Error TemplateNotFound(string code) =>
            Error.NotFound(EventCodes.Messaging.TemplateNotFound, $"Message template {code} not found");

        public static Error TemplateInvalid(string code) =>
            Error.Validation(EventCodes.Messaging.TemplateInvalid, $"Message template {code} cannot be rendered",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["template"] = ["validation.messaging.templateInvalid"] });

        public static Error RecipientInvalid() =>
            Error.Validation(EventCodes.Messaging.RecipientInvalid, "The recipient is not valid for the channel",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { ["recipient"] = ["validation.messaging.recipientInvalid"] });

        public static Error DefaultAccountMustBeActive() =>
            Error.Conflict(EventCodes.Messaging.DefaultAccountMustBeActive, "The default account of a channel must be active");

        public static Error OutboundMessageNotFound() =>
            Error.NotFound(EventCodes.Messaging.OutboundMessageNotFound, "Outbound message not found");

        public static Error SenderRuleInvalid(int index) =>
            Error.Validation(EventCodes.Messaging.SenderRuleInvalid, $"Sender rule {index} is not valid",
                new Dictionary<string, string[]>(StringComparer.Ordinal) { [$"rules[{index}]"] = ["validation.messaging.ruleInvalid"] });

        public static Error AccountAuthenticationFailed() =>
            Error.Failure(EventCodes.Messaging.AccountAuthenticationFailed, "The server refused the account credentials");

        public static Error TestDeliveryFailed() =>
            Error.Failure(EventCodes.Messaging.TestDeliveryFailed, "The test message could not be delivered: the server did not answer");
    }
}
