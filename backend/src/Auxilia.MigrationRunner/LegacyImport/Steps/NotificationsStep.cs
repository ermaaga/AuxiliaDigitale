using System.Text.Json;

using Auxilia.Application.Engagement.Public;
using Auxilia.Domain.Engagement;

using Microsoft.EntityFrameworkCore;

namespace Auxilia.MigrationRunner.LegacyImport.Steps;

/// <summary>
/// E-05 (F16): <c>Notifications</c> → <c>engagement.notifications</c> of kind <see cref="NotificationKinds.LegacyMessage"/>
/// (the legacy title and message as parameters), read ones marked read at their creation, the link resolved from the
/// legacy <c>Type</c> + <c>RelatedEntityId</c> through the id map. Runs after appointments, requests and cases.
/// </summary>
internal sealed class NotificationsStep : ILegacyImportStep
{
    public const string Table = "Notifications";

    public string Name => "notifications";

    /// <summary>The page a legacy notification opens and the record it is about, when that record was migrated.</summary>
    public static (string? Link, Guid? EntityId) Target(LegacyImportContext context, string type, int? relatedEntityId)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (relatedEntityId is not { } related)
        {
            return (null, null);
        }

        return type switch
        {
            "Appointment" when context.Ids.Find(AppointmentsStep.Table, related) is { } id => ($"/appointments?open={id}", id),
            "Request" when context.Ids.Find(RequestsStep.Table, related) is { } id => ($"/requests?open={id}", id),
            "Subscription" or "SubscriptionExpiring" when context.Ids.Find(CasesStep.Table, related) is { } id => ($"/cases/{id}", id),
            _ => (null, null),
        };
    }

    public async Task RunAsync(LegacyImportContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var users = await MigratedUsers.LoadAsync(context, cancellationToken);
        var result = context.Report.For(Table);
        foreach (var legacy in await context.Legacy.Notifications.OrderBy(row => row.Id).ToListAsync(cancellationToken))
        {
            if (context.Ids.Find(Table, legacy.Id) is not null)
            {
                continue;
            }

            if (users.User(context, legacy.UserId) is not { } userId)
            {
                result.Excluded++;
                continue;
            }

            var (link, entityId) = Target(context, legacy.Type, legacy.RelatedEntityId);
            var parameters = JsonSerializer.Serialize(new Dictionary<string, string> { ["title"] = legacy.Title, ["message"] = legacy.Message });
            var createdAt = LegacyImportContext.Instant(legacy.CreatedAt);
            var notification = new Notification(context.Ids.NewId(), userId, NotificationKinds.LegacyMessage, parameters, link, entityId, createdAt);
            if (legacy.IsRead)
            {
                notification.MarkRead(createdAt);
            }

            context.Tenant.Add(notification);
            context.Ids.Add(Table, legacy.Id, notification.Id);
            result.Created++;
        }
    }
}
