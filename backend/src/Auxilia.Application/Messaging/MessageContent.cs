using Auxilia.Application.Abstractions.Channels;
using Auxilia.Application.Messaging.Public;
using Auxilia.Diagnostics;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Messaging;

internal sealed record RenderedContent(string Language, string Subject, string Body);

internal static class MessageContent
{
    /// <summary>
    /// Renders a template in the recipient's language, falling back to the tenant's default language and then
    /// English (skill auxilia-localization: messages render in the recipient's language).
    /// </summary>
    public static async Task<Result<RenderedContent>> RenderAsync(
        IMessagingData store,
        ITemplateRenderer renderer,
        MessageChannel channel,
        string code,
        string language,
        string tenantLanguage,
        IReadOnlyDictionary<string, object?> model,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in new[] { language, tenantLanguage, MessageTemplates.FallbackLanguage }.Distinct(StringComparer.Ordinal))
        {
            if (await store.FindTemplateAsync(channel, code, candidate, cancellationToken) is not { } template)
            {
                continue;
            }

            var subject = renderer.Render(template.Subject, model, html: false);
            var body = renderer.Render(template.Body, model, html: true);
            return subject is null || body is null
                ? Errors.Messaging.TemplateInvalid(code)
                : new RenderedContent(template.Language, subject.Trim(), body);
        }

        return Errors.Messaging.TemplateNotFound(code);
    }
}

internal static class DeliverySteps
{
    /// <summary>
    /// Sends through the adapter with the decrypted secret. A permanent failure becomes a failed result; transient
    /// exceptions propagate (the bus retries).
    /// </summary>
    public static async Task<Result> SendAsync(
        IMessageChannel channel, MessagingAccount account, OutboundMessage message, IAccountSecretProtector secrets, CancellationToken cancellationToken)
    {
        var secret = account.SecretProtected is { } protectedSecret ? secrets.Unprotect(protectedSecret) : null;
        try
        {
            await channel.SendAsync(new ChannelMessage(message.Recipient, message.Subject, message.Body), new ChannelAccount(account.SettingsJson, secret), cancellationToken);
            return Result.Success();
        }
        catch (ChannelPermanentException exception)
        {
            return exception.Error;
        }
    }
}
