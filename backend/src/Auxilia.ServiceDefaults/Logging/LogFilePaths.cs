using System.Globalization;

using Auxilia.SharedKernel.Tenancy;

using Serilog.Events;

namespace Auxilia.ServiceDefaults.Logging;

/// <summary>
/// Daily file of an event (decision D-17): <c>tenants/{slug}/{yyyy}/{MM}/{dd}.jsonl</c>, or <c>platform/{yyyy}/{MM}/{dd}.jsonl</c>
/// for events without a tenant. A tenant value that is not a valid slug goes to the platform file, so it can never
/// escape the log folder.
/// </summary>
public static class LogFilePaths
{
    public static string For(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        var tenant = logEvent.Properties.TryGetValue(LogProperties.TenantSlug, out var value) && value is ScalarValue { Value: string slug }
            ? slug
            : null;

        return For(tenant, logEvent.Timestamp);
    }

    public static string For(string? tenantSlug, DateTimeOffset timestamp)
    {
        var day = timestamp.UtcDateTime;
        var folder = tenantSlug is not null && IsValidSlug(tenantSlug) ? "tenants/" + tenantSlug : "platform";

        return string.Create(CultureInfo.InvariantCulture, $"{folder}/{day:yyyy}/{day:MM}/{day:dd}.jsonl");
    }

    public static bool IsValidSlug(string tenantSlug) => TenantSlug.IsValid(tenantSlug);
}
