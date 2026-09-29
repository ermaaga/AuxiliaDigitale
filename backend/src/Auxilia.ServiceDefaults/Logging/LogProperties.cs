namespace Auxilia.ServiceDefaults.Logging;

/// <summary>Names of the log properties the pipeline reads or adds (ARCHITECTURE §12).</summary>
public static class LogProperties
{
    /// <summary>Tenant of the event, set in the log scope by tenant resolution and <c>IOperationRunner</c>.</summary>
    public const string TenantSlug = "TenantSlug";

    /// <summary><c>AUX-NNNNN</c>, derived from the <c>EventId</c> of <c>Log.*</c> entries.</summary>
    public const string EventCode = "EventCode";

    public const string Application = "Application";

    public const string Version = "Version";

    public const string Host = "Host";
}
