using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Auxilia.Infrastructure.Caching;

/// <summary>Redis is optional for correctness (L1 + database keep serving): when it is down the check is Degraded, not Unhealthy.</summary>
internal sealed class RedisHealthCheck : IHealthCheck
{
    private readonly RedisConnection connection;

    public RedisHealthCheck(RedisConnection connection) => this.connection = connection;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var multiplexer = await connection.GetAsync();
        return multiplexer.IsConnected
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("Redis is not connected; serving from memory and database.");
    }
}
