using Auxilia.Api.Authorization;
using Auxilia.Api.Infrastructure;
using Auxilia.Application.Engagement;
using Auxilia.Contracts.Common;
using Auxilia.Contracts.Engagement;

namespace Auxilia.Api.Endpoints.Engagement;

/// <summary>
/// The caller's notification centre (F16): list (newest first), unread count, mark one or all as read, delete,
/// preferences per kind (in-app / e-mail). Only the caller's own notifications exist here. Module <c>engagement</c>.
/// </summary>
internal sealed class NotificationEndpoints : IModuleEndpoints
{
    public string ModuleCode => EngagementModule.ModuleCode;

    public void Map(RouteGroupBuilder module)
    {
        var notifications = module.MapGroup("/notifications").WithTags("Notifications");

        notifications.MapGet("/", ListAsync)
            .RequirePermission(EngagementPermissions.ViewNotifications)
            .WithName("ListNotifications")
            .WithSummary("The caller's notifications, newest first (unreadOnly keeps the unread ones)")
            .Produces<PagedResponse<NotificationResponse>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        notifications.MapGet("/unread-count", UnreadCountAsync)
            .RequirePermission(EngagementPermissions.ViewNotifications)
            .WithName("CountUnreadNotifications")
            .WithSummary("How many of the caller's notifications are unread (the badge)")
            .Produces<UnreadNotificationsResponse>();

        notifications.MapPost("/{id:guid}/read", MarkReadAsync)
            .RequirePermission(EngagementPermissions.ViewNotifications)
            .WithName("MarkNotificationRead")
            .WithSummary("Marks one of the caller's notifications as read")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        notifications.MapPost("/read-all", MarkAllReadAsync)
            .RequirePermission(EngagementPermissions.ViewNotifications)
            .WithName("MarkAllNotificationsRead")
            .WithSummary("Marks all the caller's notifications as read")
            .Produces(StatusCodes.Status204NoContent);

        notifications.MapDelete("/{id:guid}", DeleteAsync)
            .RequirePermission(EngagementPermissions.ViewNotifications)
            .WithName("DeleteNotification")
            .WithSummary("Deletes one of the caller's notifications")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        notifications.MapGet("/preferences", PreferencesAsync)
            .RequirePermission(EngagementPermissions.ViewNotifications)
            .WithName("GetNotificationPreferences")
            .WithSummary("Every kind of notification with the caller's choice (default: in-app on, e-mail off)")
            .Produces<IReadOnlyList<NotificationPreferenceResponse>>();

        notifications.MapPut("/preferences", SetPreferencesAsync)
            .RequirePermission(EngagementPermissions.ViewNotifications)
            .WithName("SetNotificationPreferences")
            .WithSummary("Changes the caller's preferences of the kinds listed")
            .Produces<IReadOnlyList<NotificationPreferenceResponse>>()
            .ProducesValidationProblem();
    }

    private static async Task<IResult> ListAsync(INotificationQueryService notifications, bool? unreadOnly, int? page, int? pageSize, CancellationToken cancellationToken) =>
        (await notifications.ListAsync(new NotificationListQuery(unreadOnly ?? false, page ?? 1, pageSize ?? 10), cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> UnreadCountAsync(INotificationQueryService notifications, CancellationToken cancellationToken) =>
        (await notifications.UnreadCountAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> MarkReadAsync(Guid id, INotificationManager manager, CancellationToken cancellationToken) =>
        (await manager.MarkReadAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> MarkAllReadAsync(INotificationManager manager, CancellationToken cancellationToken) =>
        (await manager.MarkAllReadAsync(cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> DeleteAsync(Guid id, INotificationManager manager, CancellationToken cancellationToken) =>
        (await manager.DeleteAsync(id, cancellationToken)).ToHttpResult(TypedResults.NoContent);

    private static async Task<IResult> PreferencesAsync(INotificationQueryService notifications, CancellationToken cancellationToken) =>
        (await notifications.PreferencesAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);

    private static async Task<IResult> SetPreferencesAsync(
        SetNotificationPreferencesRequest request, INotificationManager manager, INotificationQueryService notifications, CancellationToken cancellationToken)
    {
        var saved = await manager.SetPreferencesAsync(request, cancellationToken);
        return saved.IsFailure ? saved.Error!.ToProblem() : (await notifications.PreferencesAsync(cancellationToken)).ToHttpResult(TypedResults.Ok);
    }
}
