using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Mailbox.Public;

/// <summary>
/// The connected accounts of a user who leaves (Directory, deleting an employee; N05 privacy note): accounts, secrets and
/// every copied item (headers, events, sync runs) are deleted in the caller's transaction, the grants are revoked at the
/// providers once it has committed (best effort). Documents already filed into cases stay.
/// </summary>
public interface IConnectedAccountCleanup
{
    Task<Result> RemoveAllAsync(Guid userId, CancellationToken cancellationToken);
}
