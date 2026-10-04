using System.Globalization;

using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Abstractions.Timeline;
using Auxilia.Contracts.Engagement;
using Auxilia.Domain.Cases;

namespace Auxilia.Application.Cases;

/// <summary>
/// The cases of a client in its timeline (B-26): openings, status changes, completions and payments of the cases the
/// caller sees (F10, the scope of the lists).
/// </summary>
internal sealed class CasesTimeline(ICaseDataFactory data, CaseAccessPolicy policy, IPermissionAccess permissions) : ITimelineContributor
{
    public async Task<IReadOnlyList<TimelineEntryResponse>> EntriesAsync(Guid clientId, DateTimeOffset? before, int take, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(CasesPermissions.ViewCases, cancellationToken))
        {
            return [];
        }

        var scope = await policy.ScopeAsync(cancellationToken);
        await using var store = await data.OpenAsync(cancellationToken);
        var rows = await store.TimelineAsync(scope, clientId, before, take, cancellationToken);
        var names = await store.UserNamesAsync([.. rows.Select(row => row.ActorUserId).OfType<Guid>().Distinct()], cancellationToken);
        return [.. rows.Select(row =>
        {
            var parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["number"] = row.Number, ["service"] = row.ServiceName };
            string titleKey;
            string kind;
            if (row.Amount is { } amount)
            {
                kind = "case.payment";
                titleKey = "app.timeline.case.payment";
                parameters["amount"] = amount.ToString("0.00", CultureInfo.InvariantCulture);
                parameters["currency"] = row.Currency;
            }
            else
            {
                kind = "case.status";
                titleKey = row.ToStatus switch
                {
                    CaseStatus.Inserted when row.FromStatus is null => "app.timeline.case.opened",
                    CaseStatus.Completed when row.IsRejected => "app.timeline.case.rejected",
                    CaseStatus.Completed => "app.timeline.case.completed",
                    var status => $"app.timeline.case.status.{status}",
                };
            }

            return new TimelineEntryResponse(
                kind, row.At, titleKey, parameters, null, row.ActorUserId is { } actor ? names.GetValueOrDefault(actor) : null, $"/cases/{row.CaseId}", null, false);
        })];
    }
}
