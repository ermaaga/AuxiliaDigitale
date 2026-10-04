using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Messaging.Public;

/// <summary>
/// Public API of the Messaging module (ARCHITECTURE §8.2): other modules send messages only through it. The message
/// is rendered from its template in the recipient's language, recorded in <c>messaging.outbound_messages</c> as
/// Queued with the account resolved from (channel, purpose, sender's role), and delivered by the Worker through the
/// queue <c>auxilia.messaging</c> (outbox: committed with the caller's operation).
/// </summary>
public interface IMessageDispatcher
{
    /// <returns>The id of the outbound message.</returns>
    Task<Result<Guid>> QueueAsync(OutboundMessageRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// The same with content the caller rendered itself (marketing templates of the Marketing module, N01): the account
    /// is resolved the same way, the subject is plain text and the body HTML.
    /// </summary>
    Task<Result<Guid>> QueueContentAsync(OutboundContentRequest request, CancellationToken cancellationToken);
}

/// <param name="Language">The language the content is written in.</param>
public sealed record OutboundContentRequest(
    MessageChannel Channel,
    MessagePurpose Purpose,
    string Recipient,
    string Language,
    string Subject,
    string Body,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null);

/// <param name="TemplateCode">A template code (<c>MessageTemplates</c>).</param>
/// <param name="Language">The recipient's language; falls back to the tenant language, then English.</param>
/// <param name="Model">Template variables (Liquid).</param>
public sealed record OutboundMessageRequest(
    MessageChannel Channel,
    MessagePurpose Purpose,
    string Recipient,
    string TemplateCode,
    string Language,
    IReadOnlyDictionary<string, object?> Model,
    string? RelatedEntityType = null,
    Guid? RelatedEntityId = null);
