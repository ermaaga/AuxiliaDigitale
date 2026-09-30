using System.Diagnostics;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Tenancy;

namespace Auxilia.Application.Bus;

/// <summary>
/// Correlation id of the scope: the one of the incoming message when set by the Worker, otherwise the trace id of the
/// current request (or a new id), stable for the whole scope.
/// </summary>
public sealed class CorrelationContext : ICorrelationContext
{
    private string? correlationId;

    public string CorrelationId => correlationId ??= Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

    public void Set(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        correlationId = value;
    }
}

/// <summary>The headers of a message produced in the current scope (tenant, correlation, caller, trace).</summary>
internal sealed class OutgoingMessageHeaders
{
    private readonly ITenantContext tenantContext;
    private readonly ICurrentUser currentUser;
    private readonly ICorrelationContext correlation;

    public OutgoingMessageHeaders(ITenantContext tenantContext, ICurrentUser currentUser, ICorrelationContext correlation)
    {
        this.tenantContext = tenantContext;
        this.currentUser = currentUser;
        this.correlation = correlation;
    }

    public Dictionary<string, string> Create(Guid messageId)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MessageHeaders.MessageId] = messageId.ToString(),
            [MessageHeaders.CorrelationId] = correlation.CorrelationId,
            [MessageHeaders.ActorType] = currentUser.ActorType.ToString(),
        };

        if (tenantContext.Current is { } tenant)
        {
            headers[MessageHeaders.TenantSlug] = tenant.Slug;
        }

        if (currentUser.UserId is { } userId)
        {
            headers[MessageHeaders.UserId] = userId.ToString();
        }

        if (Activity.Current is { } activity)
        {
            headers[MessageHeaders.TraceParent] = activity.Id!;
        }

        return headers;
    }
}
