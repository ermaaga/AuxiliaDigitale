using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

using Npgsql;

namespace Auxilia.Persistence.Tenant;

/// <summary>
/// One <see cref="NpgsqlDataSource"/> (connection pool) per tenant database, kept for the process lifetime.
/// Keyed by tenant and a hash of the connection string, so a rotated secret gets a new pool.
/// </summary>
internal sealed class TenantDataSources : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<NpgsqlDataSource>> dataSources = new(StringComparer.Ordinal);

    public NpgsqlDataSource Get(Guid tenantId, string connectionString)
    {
        var key = $"{tenantId:N}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connectionString)))}";
        return dataSources.GetOrAdd(key, _ => new Lazy<NpgsqlDataSource>(() => NpgsqlDataSource.Create(connectionString))).Value;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var dataSource in dataSources.Values.Where(item => item.IsValueCreated))
        {
            await dataSource.Value.DisposeAsync();
        }

        dataSources.Clear();
    }
}
