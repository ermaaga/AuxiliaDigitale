namespace Auxilia.Application.Abstractions.Messaging;

/// <summary>Headers every outgoing message carries (skill auxilia-messaging-rebus, "Headers &amp; context").</summary>
public static class MessageHeaders
{
    public const string TenantSlug = "x-tenant-slug";

    public const string CorrelationId = "x-correlation-id";

    /// <summary>The user who caused the message (tenant or platform user); absent for system messages.</summary>
    public const string UserId = "x-user-id";

    /// <summary><c>ActorType</c> of the caller (User, Platform, System), recorded by the audit of the handler's changes.</summary>
    public const string ActorType = "x-actor-type";

    /// <summary>W3C trace context of the originating request, so the handler's trace joins it.</summary>
    public const string TraceParent = "traceparent";

    /// <summary>The message id (Rebus <c>rbs2-msg-id</c>): the idempotency key of handlers.</summary>
    public const string MessageId = "rbs2-msg-id";
}
