using System.Reflection;

using Auxilia.ServiceDefaults.Logging.Storage;

using Azure.Storage.Blobs;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Serilog;
using Serilog.Configuration;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace Auxilia.ServiceDefaults.Logging;

public static class LoggingExtensions
{
    /// <summary>
    /// Serilog pipeline of ADR 0006 / D-17: compact JSON on the console and JSON lines in daily files per tenant,
    /// event codes, host/version properties, sensitive data masking and per-tenant minimum level (D-28).
    /// </summary>
    public static IHostApplicationBuilder AddAuxiliaLogging(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = builder.Configuration.GetSection(AuxiliaLoggingOptions.SectionName).Get<AuxiliaLoggingOptions>() ?? new();
        var contentRoot = builder.Environment.ContentRootPath;

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(services =>
            new TenantLogLevels(options.MinimumLevel, services.GetService<TimeProvider>() ?? TimeProvider.System));

        builder.Services.AddSerilog((services, configuration) =>
        {
            var levels = services.GetRequiredService<TenantLogLevels>();
            ITextFormatter formatter = new RenderedCompactJsonFormatter();

            configuration
                .MinimumLevel.ControlledBy(levels.MinimumLevel)
                .Filter.With(new TenantLevelFilter(levels))
                .Enrich.FromLogContext()
                .Enrich.WithProperty(LogProperties.Application, builder.Environment.ApplicationName)
                .Enrich.WithProperty(LogProperties.Version, ApplicationVersion())
                .Enrich.WithProperty(LogProperties.Host, Environment.MachineName)
                .Enrich.With<EventCodeEnricher>()
                .Enrich.With<SensitiveDataMasker>()
                .WriteTo.Console(formatter);

            foreach (var (source, level) in options.Overrides)
            {
                configuration.MinimumLevel.Override(source, level);
            }

            var store = CreateStore(options, contentRoot);
            if (store is not null)
            {
                var sink = new TenantFileSink(
                    store,
                    new LocalFileLogStore(Path.Combine(contentRoot, options.BufferPath)),
                    formatter,
                    Console.Error,
                    services.GetService<TimeProvider>() ?? TimeProvider.System);

                configuration.WriteTo.Sink(sink, new BatchingOptions
                {
                    BatchSizeLimit = options.BatchSizeLimit,
                    BufferingTimeLimit = options.BatchPeriod,
                    QueueLimit = options.QueueLimit,
                    EagerlyEmitFirstEvent = true,
                });
            }
        });

        return builder;
    }

    private static ILogFileStore? CreateStore(AuxiliaLoggingOptions options, string contentRoot) => options.Storage switch
    {
        AuxiliaLoggingOptions.LocalFileStorage => new LocalFileLogStore(Path.Combine(contentRoot, options.LocalPath)),
        AuxiliaLoggingOptions.AzureBlobStorage => new AzureAppendBlobLogStore(CreateContainer(options.AzureBlob)),
        AuxiliaLoggingOptions.NoStorage => null,
        _ => throw new InvalidOperationException(
            $"{AuxiliaLoggingOptions.SectionName}:Storage must be '{AuxiliaLoggingOptions.LocalFileStorage}', " +
            $"'{AuxiliaLoggingOptions.AzureBlobStorage}' or '{AuxiliaLoggingOptions.NoStorage}', not '{options.Storage}'."),
    };

    private static BlobContainerClient CreateContainer(AzureBlobLogOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                $"{AuxiliaLoggingOptions.SectionName}:AzureBlob:ConnectionString is required when Storage is '{AuxiliaLoggingOptions.AzureBlobStorage}'.");
        }

        return Uri.TryCreate(options.ConnectionString, UriKind.Absolute, out var sasUri) && sasUri.Scheme == Uri.UriSchemeHttps
            ? new BlobContainerClient(sasUri)
            : new BlobContainerClient(options.ConnectionString, options.Container);
    }

    private static string ApplicationVersion() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
}
