namespace Auxilia.Contracts.Messages.V1.Mailbox;

/// <summary>
/// Synchronise one account and scope (N05, #106): <c>Part</c> 1 is the run queued by the requester, the next parts are
/// the continuations queued when a part used its time budget. The run id is all the Worker needs.
/// </summary>
public sealed record SyncConnectedAccountCommand(Guid RunId, int Part) : ITenantMessage;
