using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Abstractions.Operations;

/// <summary>
/// Runs every Manager operation the same way (ADR 0004, ARCHITECTURE §4bis): trace, log scope, transaction + outbox
/// for writes, metrics, exactly one outcome log with an event code, mapping of known exceptions (ADR 0012).
/// Managers must not re-implement any of these concerns.
/// </summary>
public interface IOperationRunner
{
    /// <param name="context">Values that identify the operation input (ids, not payloads), added to the log scope and trace.</param>
    Task<Result<TValue>> RunAsync<TValue>(
        OperationDescriptor operation,
        object? context,
        Func<IOperationScope, Task<Result<TValue>>> work,
        CancellationToken cancellationToken);

    Task<Result> RunAsync(
        OperationDescriptor operation,
        object? context,
        Func<IOperationScope, Task<Result>> work,
        CancellationToken cancellationToken);
}

/// <summary>The running operation, available to the work delegate.</summary>
public interface IOperationScope
{
    OperationDescriptor Operation { get; }

    /// <summary>Adds the affected entity (e.g. <c>SetEntity("Case", id)</c> → <c>CaseId</c>) to the log scope and the trace.</summary>
    void SetEntity(string entityType, Guid id);

    /// <summary>
    /// Runs <paramref name="action"/> once the outermost operation of the scope has succeeded and committed (e.g. cache
    /// invalidation, skill auxilia-caching); discarded when it fails. A failing action is logged (<c>AUX-10021</c>) and
    /// does not change the result.
    /// </summary>
    void OnCommitted(Func<CancellationToken, Task> action);
}
