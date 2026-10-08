using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Domain.Platform;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Auxilia.Infrastructure.Tenancy;

/// <summary>
/// Readiness (F32): Degraded while an active or suspended tenant has not reached the newest schema migration of this
/// build (run <c>auxctl migrate tenants</c>); the other tenants keep working. Only counts and slugs, no data.
/// </summary>
internal sealed class TenantSchemaHealthCheck : IHealthCheck
{
    public const string Name = "tenant-schemas";

    private readonly ICatalogStore catalog;
    private readonly ITenantSchemaInfo schema;

    public TenantSchemaHealthCheck(ICatalogStore catalog, ITenantSchemaInfo schema)
    {
        this.catalog = catalog;
        this.schema = schema;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (schema.LatestSchemaVersion is not { } latest)
        {
            return HealthCheckResult.Healthy();
        }

        var behind = (await catalog.ListTenantsAsync(cancellationToken))
            .Where(tenant => tenant.Status is TenantStatus.Active or TenantStatus.Suspended
                && !string.Equals(tenant.SchemaVersion, latest, StringComparison.Ordinal))
            .Select(tenant => tenant.Slug)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var data = new Dictionary<string, object> { ["latestSchemaVersion"] = latest, ["tenantsBehind"] = behind };
        return behind.Length == 0
            ? HealthCheckResult.Healthy(data: data)
            : HealthCheckResult.Degraded($"{behind.Length} tenant(s) behind schema {latest}: {string.Join(", ", behind)}", data: data);
    }
}

public static class TenantSchemaHealthCheckRegistration
{
    /// <summary>For the Api, which has the Catalog, the tenant persistence and the health endpoints: readiness of the tenant schemas (F32).</summary>
    public static IServiceCollection AddTenantSchemaHealthCheck(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<TenantSchemaHealthCheck>(TenantSchemaHealthCheck.Name);
        return services;
    }
}
