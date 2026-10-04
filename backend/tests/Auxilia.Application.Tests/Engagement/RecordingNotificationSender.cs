using Auxilia.Application.Engagement.Public;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Tests.Engagement;

/// <summary>Records what producers ask to notify (the sender itself is tested in <c>NotificationTests</c>).</summary>
internal sealed class RecordingNotificationSender : INotificationSender
{
    public List<(string Target, NotificationMessage Message)> Sent { get; } = [];

    public Task NotifyUsersAsync(IReadOnlyCollection<Guid> userIds, NotificationMessage message, CancellationToken cancellationToken)
    {
        Sent.AddRange(userIds.Select(userId => ($"user:{userId}", message)));
        return Task.CompletedTask;
    }

    public Task NotifyRoleAsync(TenantRole role, NotificationMessage message, CancellationToken cancellationToken)
    {
        Sent.Add(($"role:{role}", message));
        return Task.CompletedTask;
    }
}
