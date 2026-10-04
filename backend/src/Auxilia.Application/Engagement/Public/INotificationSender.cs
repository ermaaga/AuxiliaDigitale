using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Engagement.Public;

/// <summary>
/// A notification to send (F16): <paramref name="Kind"/> one of <see cref="NotificationKinds"/>, the record it is about
/// and the values of its text placeholders.
/// </summary>
public sealed record NotificationMessage(string Kind, Guid? EntityId, IReadOnlyDictionary<string, string> Parameters);

/// <summary>
/// Public API of the notifications (Engagement, F16) for the modules that produce them (appointments, requests,
/// registrations…). Called inside the producer's write operation: the rows join its transaction, the realtime push
/// (<c>NotificationReceived</c>) and the e-mails follow the commit. Each recipient's preferences decide in-app and
/// e-mail; the acting user is never notified.
/// </summary>
public interface INotificationSender
{
    Task NotifyUsersAsync(IReadOnlyCollection<Guid> userIds, NotificationMessage message, CancellationToken cancellationToken);

    /// <summary>Every active user with the role (e.g. the office: the Administrators).</summary>
    Task NotifyRoleAsync(TenantRole role, NotificationMessage message, CancellationToken cancellationToken);
}
