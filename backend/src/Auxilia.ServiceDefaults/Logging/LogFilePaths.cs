using System.Globalization;
using System.Text.RegularExpressions;

using Serilog.Events;

namespace Auxilia.ServiceDefaults.Logging;

/// <summary>
/// Daily file of an event (decision D-17): <c>tenants/{slug}/{yyyy}/{MM}/{dd}.jsonl</c>, or <c>platform/{yyyy}/{MM}/{dd}.jsonl</c>
/// for events without a tenant. A tenant value that is not a valid slug goes to the platform file, so it can never
/// escape the log folder.
/// </summary>
public static partial class LogFilePaths
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

    public static bool IsValidSlug(string tenantSlug) => SlugPattern().IsMatch(tenantSlug);

    // Same rule as tenant provisioning: lowercase letters, digits and inner hyphens, 2–63 characters.
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,61}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
