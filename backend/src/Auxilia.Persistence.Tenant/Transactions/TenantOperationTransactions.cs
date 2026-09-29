using System.Data.Common;

using Auxilia.Application.Abstractions.Operations;

using Npgsql;

namespace Auxilia.Persistence.Tenant.Transactions;

/// <summary>
/// Transaction of the write operation running in this scope (<see cref="IOperationRunner"/>, ADR 0004). The database
/// connection is opened lazily by the first tenant context the operation creates; every context of the operation
/// shares it, and the runner commits once at the end (rolls back when the operation fails or throws). Nested
/// operations join the outer transaction.
/// </summary>
internal sealed class TenantOperationTransactions : IOperationTransactionFactory, IAsyncDisposable
{
    private int depth;
    private NpgsqlDataSource? dataSource;
    private NpgsqlConnection? connection;
    private NpgsqlTransaction? transaction;

    public bool IsActive => depth > 0;

    public Task<IOperationTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        depth++;
        return Task.FromResult<IOperationTransaction>(new Handle(this, isOuter: depth == 1));
    }

    /// <summary>The shared connection and transaction for a context created inside the operation.</summary>
    public async Task<(DbConnection Connection, DbTransaction Transaction)> EnlistAsync(NpgsqlDataSource source, CancellationToken cancellationToken)
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("No operation transaction is active.");
        }

        if (dataSource is not null && !ReferenceEquals(dataSource, source))
        {
            throw new InvalidOperationException("An operation cannot write to two tenant databases.");
        }

        if (connection is null)
        {
            dataSource = source;
            connection = await source.OpenConnectionAsync(cancellationToken);
            transaction = await connection.BeginTransactionAsync(cancellationToken);
        }

        return (connection, transaction!);
    }

    public async ValueTask DisposeAsync() => await ResetAsync();

    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        await ResetAsync();
    }

    private async Task EndAsync()
    {
        depth--;
        if (depth == 0)
        {
            // Not committed: disposing the transaction rolls it back.
            await ResetAsync();
        }
    }

    private async Task ResetAsync()
    {
        if (transaction is not null)
        {
            await transaction.DisposeAsync();
            transaction = null;
        }

        if (connection is not null)
        {
            await connection.DisposeAsync();
            connection = null;
        }

        dataSource = null;
    }

    private sealed class Handle(TenantOperationTransactions owner, bool isOuter) : IOperationTransaction
    {
        private bool ended;

        public Task CommitAsync(CancellationToken cancellationToken) =>
            isOuter ? owner.CommitAsync(cancellationToken) : Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            if (!ended)
            {
                ended = true;
                await owner.EndAsync();
            }
        }
    }
}
