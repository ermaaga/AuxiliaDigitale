using System.Globalization;

using Auxilia.Application.Abstractions.Cases;
using Auxilia.Application.Abstractions.Jobs;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Directory.Public;
using Auxilia.Application.Engagement.Public;
using Auxilia.Diagnostics;
using Auxilia.Domain.Cases;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Cases;

/// <summary>
/// The legacy subscription expiry (F11, D-15: run by hand, never scheduled), when <c>cases.expiry.enabled</c>: active
/// cases whose expiry date has passed become inactive and the client status is recomputed (Q03; sign-in unchanged,
/// D-05); the clients are told in the app. Clients of cases ending within <c>cases.expiry.expiringDays</c> are told
/// once a day. Days in the tenant time zone.
/// </summary>
internal sealed class CaseExpiryJob(
    IOperationRunner operations,
    ICaseDataFactory data,
    IClientDirectory clients,
    INotificationSender notifications,
    ISettingsProvider settings,
    ITenantContext tenant,
    TimeProvider clock) : IRecurringJob
{
    public const string JobCode = "cases.expiry";

    public string Code => JobCode;

    public string Description => "Deactivates the expired cases and tells the clients of the cases expiring soon";

    public string SuggestedFrequency => "daily";

    public Task<Result<string>> RunAsync(CancellationToken cancellationToken) =>
        operations.RunAsync<string>(Operations.Cases.RunCasesExpiry, null, async _ =>
        {
            if (!await settings.GetAsync(CasesSettings.ExpiryEnabled, cancellationToken))
            {
                return "disabled";
            }

            var zone = TimeZoneInfo.TryFindSystemTimeZoneById(tenant.Tenant.TimeZone, out var found) ? found : TimeZoneInfo.Utc;
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime);
            var horizon = today.AddDays(await settings.GetAsync(CasesSettings.ExpiryExpiringDays, cancellationToken));

            await using var store = await data.OpenAsync(cancellationToken);
            var expired = (await store.ExpiredActiveAsync(today, cancellationToken)).Where(@case => @case.ExpireIfDue(today)).ToArray();
            var expiring = (await store.ExpiringAsync(today, horizon, cancellationToken)).Where(@case => @case.MarkExpiryNotified(today)).ToArray();
            await store.SaveChangesAsync(cancellationToken);

            foreach (var clientId in expired.Select(@case => @case.ClientId).Distinct())
            {
                var status = await clients.UpdateStatusAsync(clientId, await store.HasOpenCasesAsync(clientId, today, cancellationToken), cancellationToken);
                if (status.IsFailure)
                {
                    return Result.Failure<string>(status.Error!);
                }
            }

            var users = await store.ClientUsersAsync([.. expired.Concat(expiring).Select(@case => @case.ClientId).Distinct()], cancellationToken);
            var names = new Dictionary<Guid, string>();
            foreach (var @case in expired)
            {
                await NotifyAsync(store, users, names, @case, NotificationKinds.CaseExpired, @case.ExpiresOn!.Value, today, cancellationToken);
            }

            foreach (var @case in expiring)
            {
                await NotifyAsync(store, users, names, @case, NotificationKinds.CaseExpiring, @case.EndsOn!.Value, today, cancellationToken);
            }

            return string.Create(CultureInfo.InvariantCulture, $"expired {expired.Length}, expiring {expiring.Length}");
        }, cancellationToken);

    private async Task NotifyAsync(
        ICaseData store, IReadOnlyDictionary<Guid, Guid> users, Dictionary<Guid, string> serviceNames, Case @case, string kind, DateOnly endsOn, DateOnly today,
        CancellationToken cancellationToken)
    {
        if (!users.TryGetValue(@case.ClientId, out var userId))
        {
            return;
        }

        if (!serviceNames.TryGetValue(@case.ServiceId, out var service))
        {
            service = (await store.ServiceAsync(@case.ServiceId, cancellationToken))?.Name ?? string.Empty;
            serviceNames[@case.ServiceId] = service;
        }

        await notifications.NotifyUsersAsync(
            [userId],
            new NotificationMessage(
                kind,
                @case.Id,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["service"] = service,
                    ["number"] = @case.Number,
                    ["date"] = endsOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                    ["days"] = (endsOn.DayNumber - today.DayNumber).ToString(CultureInfo.InvariantCulture),
                }),
            cancellationToken);
    }
}
