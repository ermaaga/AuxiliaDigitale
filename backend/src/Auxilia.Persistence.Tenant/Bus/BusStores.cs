using System.Text.Json;

using Auxilia.Application.Abstractions.Jobs;
using Auxilia.Application.Abstractions.Messaging;
using Auxilia.Application.Abstractions.Persistence;
using Auxilia.Persistence.Tenant.Operations;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Auxilia.Persistence.Tenant.Bus;

/// <inheritdoc cref="IOutboxStore"/>
internal sealed class OutboxStore : IOutboxStore
{
    private readonly ITenantDbContextFactory databases;
    private readonly TenantDbContextFactory independent;

    public OutboxStore(ITenantDbContextFactory databases, TenantDbContextFactory independent)
    {
        this.databases = databases;
        this.independent = independent;
    }

    public async Task AddAsync(OutboxEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var db = await databases.CreateAsync(cancellationToken);
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = entry.Id,
            MessageType = entry.MessageType,
            Body = entry.Body,
            Headers = JsonSerializer.Serialize(entry.Headers),
            CreatedAt = entry.CreatedAt,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(
        IReadOnlyCollection<Guid>? ids, DateTimeOffset createdBefore, int maxCount, CancellationToken cancellationToken)
    {
        await using var db = await independent.CreateOutsideOperationAsync(cancellationToken);
        var pending = db.Set<OutboxMessage>().AsNoTracking().Where(message => message.DispatchedAt == null);
        pending = ids is null
            ? pending.Where(message => message.CreatedAt <= createdBefore)
            : pending.Where(message => ids.Contains(message.Id));

        var rows = await pending.OrderBy(message => message.CreatedAt).ThenBy(message => message.Id).Take(maxCount).ToListAsync(cancellationToken);
        return rows
            .Select(row => new OutboxEntry(row.Id, row.MessageType, row.Body, JsonSerializer.Deserialize<Dictionary<string, string>>(row.Headers)!, row.CreatedAt))
            .ToArray();
    }

    public async Task MarkDispatchedAsync(Guid id, DateTimeOffset dispatchedAt, CancellationToken cancellationToken)
    {
        await using var db = await independent.CreateOutsideOperationAsync(cancellationToken);
        await db.Set<OutboxMessage>()
            .Where(message => message.Id == id && message.DispatchedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.DispatchedAt, dispatchedAt), cancellationToken);
    }
}

/// <inheritdoc cref="IProcessedMessageStore"/>
internal sealed class ProcessedMessageStore : IProcessedMessageStore
{
    private readonly ITenantDbContextFactory databases;
    private readonly TimeProvider timeProvider;

    public ProcessedMessageStore(ITenantDbContextFactory databases, TimeProvider timeProvider)
    {
        this.databases = databases;
        this.timeProvider = timeProvider;
    }

    public async Task<bool> TryMarkProcessedAsync(Guid messageId, string handler, CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        var inserted = await ((DbContext)db).Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO ops.processed_messages (message_id, handler, processed_at)
            VALUES ({messageId}, {handler}, {timeProvider.GetUtcNow()})
            ON CONFLICT (message_id, handler) DO NOTHING
            """,
            cancellationToken);
        return inserted == 1;
    }

    public async Task<bool> IsProcessedAsync(Guid messageId, string handler, CancellationToken cancellationToken)
    {
        await using var db = await databases.CreateAsync(cancellationToken);
        return await db.Set<ProcessedMessage>().AnyAsync(message => message.MessageId == messageId && message.Handler == handler, cancellationToken);
    }
}

/// <summary>
/// PostgreSQL session advisory lock in the tenant database (key = hash of <c>job:{code}</c>), held on a dedicated
/// connection until disposed. Works for every Api/Worker/auxctl node without Redis.
/// </summary>
internal sealed class PostgresJobLock : IJobLock
{
    private readonly TenantDbContextFactory databases;

    public PostgresJobLock(TenantDbContextFactory databases) => this.databases = databases;

    public async Task<IAsyncDisposable?> TryAcquireAsync(string jobCode, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobCode);

        var dataSource = await databases.DataSourceAsync(cancellationToken);
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtextextended(@key, 0))", connection);
            command.Parameters.AddWithValue("key", "job:" + jobCode);
            if (await command.ExecuteScalarAsync(cancellationToken) is true)
            {
                return new Held(connection, "job:" + jobCode);
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }

        await connection.DisposeAsync();
        return null;
    }

    private sealed class Held(NpgsqlConnection connection, string key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtextextended(@key, 0))", connection);
                command.Parameters.AddWithValue("key", key);
                await command.ExecuteScalarAsync();
            }
            finally
            {
                // Closing the session also releases the lock if the unlock failed.
                await connection.DisposeAsync();
            }
        }
    }
}
