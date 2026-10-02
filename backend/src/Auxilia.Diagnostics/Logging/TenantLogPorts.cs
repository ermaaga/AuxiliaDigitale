namespace Auxilia.Diagnostics.Logging;

/// <summary>
/// The per-tenant minimum level of this process (decision D-28), implemented by the logging pipeline of the host
/// (ServiceDefaults) and set by the Platform module from the Catalog. Lives here because both sides see Diagnostics.
/// </summary>
public interface ITenantLogLevels
{
    /// <summary>The level of every tenant without an override (Serilog name, e.g. <c>Information</c>).</summary>
    string DefaultLevel { get; }

    /// <summary>Writes Debug events of the tenant until <paramref name="until"/>; a past instant clears the override.</summary>
    void EnableDebug(string tenantSlug, DateTimeOffset until);

    /// <summary>Back to the default level for the tenant (no override: nothing happens).</summary>
    void Clear(string tenantSlug);
}

/// <summary>
/// Reads the daily log files written by the pipeline (D-17): <c>tenants/{slug}/{yyyy}/{MM}/{dd}.jsonl</c>, one JSON
/// object per line (CLEF). Read-only and storage independent (<c>local-file</c>, <c>azure-blob</c>).
/// </summary>
public interface ILogFileReader
{
    /// <summary>The lines of the tenant's file of <paramref name="day"/> (UTC) in write order; none when the file does not exist.</summary>
    IAsyncEnumerable<string> ReadTenantDayAsync(string tenantSlug, DateOnly day, CancellationToken cancellationToken);
}
