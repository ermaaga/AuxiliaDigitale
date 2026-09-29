using Auxilia.Application.Abstractions.Operations;

namespace Auxilia.Application.Execution;

/// <summary>Default until persistence exists (task P1-08 registers the EF Core transaction + outbox).</summary>
internal sealed class NoOperationTransactionFactory : IOperationTransactionFactory
{
    public Task<IOperationTransaction> BeginAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IOperationTransaction>(NoTransaction.Instance);

    private sealed class NoTransaction : IOperationTransaction
    {
        public static readonly NoTransaction Instance = new();

        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
