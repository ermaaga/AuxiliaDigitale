using System.Text.Json;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Engagement;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Realtime;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Engagement.Public;
using Auxilia.Application.Identity.Public;
using Auxilia.Application.Localization.Public;
using Auxilia.Application.Messaging.Public;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Engagement;
using Auxilia.Contracts.Realtime;
using Auxilia.Diagnostics;
using Auxilia.Domain.Engagement;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Engagement;

/// <summary>The caller's own notifications (F16): read one or all, delete, preferences per kind.</summary>
public interface INotificationManager
{
    Task<Result> MarkReadAsync(Guid id, CancellationToken cancellationToken);

    Task<Result> MarkAllReadAsync(CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<Result> SetPreferencesAsync(SetNotificationPreferencesRequest request, CancellationToken cancellationToken);
}

public interface INotificationQueryService
{
    Task<Result<PagedResponse<NotificationResponse>>> ListAsync(NotificationListQuery query, CancellationToken cancellationToken);

    Task<Result<UnreadNotificationsResponse>> UnreadCountAsync(CancellationToken cancellationToken);

    /// <summary>Every kind with the caller's choice (or the defaults: in-app on, e-mail off).</summary>
    Task<Result<IReadOnlyList<NotificationPreferenceResponse>>> PreferencesAsync(CancellationToken cancellationToken);
}

/// <inheritdoc cref="INotificationSender"/>
internal sealed class NotificationSender(
    IOperationRunner operations,
    INotificationDataFactory data,
    IRealtimeNotifier notifier,
    IMessageDispatcher messages,
    ILocalizer localizer,
    ISettingsProvider settings,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock) : INotificationSender
{
    public async Task NotifyUsersAsync(IReadOnlyCollection<Guid> userIds, NotificationMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        await SendAsync(message, store => store.RecipientsAsync(userIds.Distinct().ToArray(), cancellationToken), cancellationToken);
    }

    public Task NotifyRoleAsync(TenantRole role, NotificationMessage message, CancellationToken cancellationToken) =>
        SendAsync(message, store => store.RecipientsWithRoleAsync(role, cancellationToken), cancellationToken);

    private async Task SendAsync(
        NotificationMessage message, Func<INotificationData, Task<IReadOnlyList<NotificationRecipient>>> recipientsOf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // A notification never fails the operation that produced it: the outcome is only logged.
        _ = await operations.RunAsync(Operations.Notifications.SendNotifications, new { message.Kind, message.EntityId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var recipients = (await recipientsOf(store)).Where(recipient => recipient.UserId != currentUser.UserId).ToArray();
            if (recipients.Length == 0)
            {
                return Result.Success();
            }

            var preferences = await store.PreferencesAsync(recipients.Select(recipient => recipient.UserId).ToArray(), message.Kind, cancellationToken);
            var now = clock.GetUtcNow();
            var link = NotificationKinds.Link(message.Kind, message.EntityId);
            var parameters = JsonSerializer.Serialize(message.Parameters);
            var emails = new List<NotificationRecipient>();
            foreach (var recipient in recipients)
            {
                var preference = preferences.GetValueOrDefault(recipient.UserId);
                if (preference?.InApp ?? true)
                {
                    var notification = new Notification(Guid.CreateVersion7(), recipient.UserId, message.Kind, parameters, link, message.EntityId, now);
                    store.Add(notification);
                    var pushed = new NotificationReceivedEvent(notification.Id, message.Kind, message.Parameters, link, now);
                    scope.OnCommitted(ct => notifier.ToUserAsync(recipient.UserId, RealtimeEvents.NotificationReceived, pushed, ct));
                }

                if ((preference?.Email ?? false) && !string.IsNullOrWhiteSpace(recipient.Email))
                {
                    emails.Add(recipient);
                }
            }

            await store.SaveChangesAsync(cancellationToken);
            foreach (var recipient in emails)
            {
                await EmailAsync(recipient, message, link, cancellationToken);
            }

            return Result.Success();
        }, cancellationToken);
    }

    /// <summary>The notification as an e-mail in the recipient's language (generic template <c>notification</c>).</summary>
    private async Task EmailAsync(NotificationRecipient recipient, NotificationMessage message, string? link, CancellationToken cancellationToken)
    {
        var arguments = message.Parameters.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
        var title = await localizer.GetAsync($"notifications.{message.Kind}.title", recipient.Language, arguments, cancellationToken);
        var body = await localizer.GetAsync($"notifications.{message.Kind}.message", recipient.Language, arguments, cancellationToken);
        var baseUrl = await settings.GetAsync(IdentityLinks.AppBaseUrl, cancellationToken);
        var slug = tenant.Tenant.Slug;
        _ = await messages.QueueAsync(
            new OutboundMessageRequest(
                MessageChannel.Email,
                MessagePurpose.Notification,
                recipient.Email!,
                MessageTemplates.Notification,
                recipient.Language,
                new Dictionary<string, object?>
                {
                    ["title"] = title,
                    ["message"] = body,
                    ["link"] = $"{baseUrl}/{slug}{link ?? "/dashboard"}",
                    ["appName"] = slug,
                },
                nameof(Notification),
                message.EntityId),
            cancellationToken);
    }
}

internal sealed class NotificationManager(IOperationRunner operations, INotificationDataFactory data, ICurrentUser currentUser, TimeProvider clock) : INotificationManager
{
    public Task<Result> MarkReadAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Notifications.MarkNotificationRead, new { NotificationId = id }, async _ =>
        {
            if (currentUser.UserId is not { } me)
            {
                return Errors.Identity.PermissionDenied();
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(me, id, cancellationToken) is not { } notification)
            {
                return Errors.Notifications.NotificationNotFound();
            }

            if (notification.MarkRead(clock.GetUtcNow()))
            {
                await store.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }, cancellationToken);

    public Task<Result> MarkAllReadAsync(CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Notifications.MarkAllNotificationsRead, new { }, async _ =>
        {
            if (currentUser.UserId is not { } me)
            {
                return Errors.Identity.PermissionDenied();
            }

            await using var store = await data.OpenAsync(cancellationToken);
            await store.MarkAllReadAsync(me, clock.GetUtcNow(), cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Notifications.DeleteNotification, new { NotificationId = id }, async _ =>
        {
            if (currentUser.UserId is not { } me)
            {
                return Errors.Identity.PermissionDenied();
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(me, id, cancellationToken) is not { } notification)
            {
                return Errors.Notifications.NotificationNotFound();
            }

            store.Remove(notification);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> SetPreferencesAsync(SetNotificationPreferencesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Notifications.SaveNotificationPreferences, new { Count = request.Items?.Count ?? 0 }, async _ =>
        {
            if (currentUser.UserId is not { } me)
            {
                return Errors.Identity.PermissionDenied();
            }

            var items = request.Items ?? [];
            var unknown = items.Select((item, index) => (item, index)).Where(pair => !NotificationKinds.All.Contains(pair.item.Kind)).ToArray();
            if (unknown.Length > 0 || items.Select(item => item.Kind).Distinct(StringComparer.Ordinal).Count() != items.Count)
            {
                return Errors.Notifications.NotificationPreferencesInvalid(
                    new Dictionary<string, string[]>(StringComparer.Ordinal) { ["items"] = ["validation.notifications.kind"] });
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var stored = (await store.PreferencesOfAsync(me, cancellationToken)).ToDictionary(preference => preference.Kind, StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (stored.TryGetValue(item.Kind, out var preference))
                {
                    preference.Set(item.InApp, item.Email);
                }
                else
                {
                    store.Add(new NotificationPreference(me, item.Kind, item.InApp, item.Email));
                }
            }

            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);
    }
}

internal sealed class NotificationQueryService(INotificationDataFactory data, ICurrentUser currentUser) : INotificationQueryService
{
    public const int MaxPageSize = 100;

    public async Task<Result<PagedResponse<NotificationResponse>>> ListAsync(NotificationListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (query.Page < 1)
        {
            errors["page"] = ["validation.paging.page"];
        }

        if (query.PageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = ["validation.paging.pageSize"];
        }

        if (errors.Count > 0)
        {
            return Errors.Host.ValidationFailed(errors);
        }

        if (currentUser.UserId is not { } me)
        {
            return Errors.Identity.PermissionDenied();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var (items, total) = await store.PageAsync(me, query.UnreadOnly, (query.Page - 1) * query.PageSize, query.PageSize, cancellationToken);
        return new PagedResponse<NotificationResponse>(items.Select(ToResponse).ToArray(), query.Page, query.PageSize, total);
    }

    public async Task<Result<UnreadNotificationsResponse>> UnreadCountAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } me)
        {
            return Errors.Identity.PermissionDenied();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        return new UnreadNotificationsResponse(await store.UnreadCountAsync(me, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<NotificationPreferenceResponse>>> PreferencesAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } me)
        {
            return Errors.Identity.PermissionDenied();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        var stored = (await store.PreferencesOfAsync(me, cancellationToken)).ToDictionary(preference => preference.Kind, StringComparer.Ordinal);
        return NotificationKinds.All
            .Select(kind => stored.TryGetValue(kind, out var preference)
                ? new NotificationPreferenceResponse(kind, preference.InApp, preference.Email)
                : new NotificationPreferenceResponse(kind, InApp: true, Email: false))
            .ToArray();
    }

    private static NotificationResponse ToResponse(Notification notification)
    {
        using var parameters = JsonDocument.Parse(notification.Parameters);
        return new NotificationResponse(
            notification.Id, notification.Kind, parameters.RootElement.Clone(), notification.Link, notification.EntityId, notification.CreatedAt, notification.IsRead);
    }
}
