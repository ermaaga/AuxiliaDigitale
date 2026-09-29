namespace Auxilia.Application.Abstractions.Operations;

/// <summary>
/// Transaction + outbox of a write operation, opened by <see cref="IOperationRunner"/>. Disposing without
/// <see cref="CommitAsync"/> rolls back. Implemented by the persistence layer (task P1-08).
/// </summary>
public interface IOperationTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public interface IOperationTransactionFactory
{
    Task<IOperationTransaction> BeginAsync(CancellationToken cancellationToken);
}
