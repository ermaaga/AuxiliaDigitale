using Auxilia.Diagnostics;
using Auxilia.ServiceDefaults.Logging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Auxilia.ServiceDefaults;

/// <summary>
/// Cross-cutting host setup shared by Api and Worker (ADR 0013, the role Aspire's ServiceDefaults would have):
/// logging (ADR 0006), OpenTelemetry, health checks and HTTP resilience.
/// </summary>
public static class ServiceDefaultsExtensions
{
    public const string LivePath = "/health/live";

    public const string ReadyPath = "/health/ready";

    /// <summary>Tag of the checks that only prove the process is alive (no dependencies).</summary>
    public const string LiveTag = "live";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddAuxiliaLogging();
        builder.AddAuxiliaTelemetry();

        // Dependency checks (Postgres, Valkey, RabbitMQ, tenant schema versions) are added by the tasks that introduce them.
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);

        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        return builder;
    }

    /// <summary>
    /// Traces and metrics of the Auxilia source/meter, ASP.NET Core, HttpClient and runtime. Exported over OTLP only when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set: the backend is decided with D-12.
    /// </summary>
    public static TBuilder AddAuxiliaTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(builder.Environment.ApplicationName))
            .WithTracing(tracing => tracing
                .AddSource(Telemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !IsHealthRequest(context))
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter(Telemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter();
        }

        return builder;
    }

    /// <summary><c>/health/live</c> (process alive) and <c>/health/ready</c> (all checks), for container probes.</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks(LivePath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(LiveTag) });
        app.MapHealthChecks(ReadyPath);

        return app;
    }

    private static bool IsHealthRequest(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
}
