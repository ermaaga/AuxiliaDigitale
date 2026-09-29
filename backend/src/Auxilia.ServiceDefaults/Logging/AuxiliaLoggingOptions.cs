using Serilog.Events;

namespace Auxilia.ServiceDefaults.Logging;

/// <summary>Infrastructure logging settings (section <c>AuxiliaLogging</c>); per-tenant levels come from the tenant settings (D-28).</summary>
public sealed class AuxiliaLoggingOptions
{
    public const string SectionName = "AuxiliaLogging";

    public const string LocalFileStorage = "local-file";

    public const string AzureBlobStorage = "azure-blob";

    public const string NoStorage = "none";

    /// <summary>Default minimum level (Information in every environment, D-17).</summary>
    public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

    /// <summary>Minimum level per source namespace, e.g. <c>Microsoft.AspNetCore = Warning</c>.</summary>
    public Dictionary<string, LogEventLevel> Overrides { get; set; } = new(StringComparer.Ordinal);

    /// <summary><c>local-file</c> (development), <c>azure-blob</c> (production) or <c>none</c> (console only, e.g. tests).</summary>
    public string Storage { get; set; } = LocalFileStorage;

    /// <summary>Root folder of <c>local-file</c>, relative to the content root.</summary>
    public string LocalPath { get; set; } = "logs";

    /// <summary>Local folder that receives the batches when the storage fails, relative to the content root.</summary>
    public string BufferPath { get; set; } = "logs-buffer";

    public AzureBlobLogOptions AzureBlob { get; set; } = new();

    public int BatchSizeLimit { get; set; } = 1000;

    public TimeSpan BatchPeriod { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Events kept in memory while the storage is slow; beyond this, new events are dropped.</summary>
    public int QueueLimit { get; set; } = 100_000;
}

public sealed class AzureBlobLogOptions
{
    /// <summary>Storage account connection string or container SAS URI; kept in the secret store, never in appsettings.</summary>
    public string? ConnectionString { get; set; }

    public string Container { get; set; } = "logs";
}
