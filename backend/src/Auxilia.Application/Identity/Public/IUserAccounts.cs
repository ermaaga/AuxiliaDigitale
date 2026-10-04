using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Identity.Public;

/// <summary>A user account as other modules see it (no password data).</summary>
/// <param name="IsActivated">The account has a password (activation done, D-06).</param>
/// <param name="LanguageCode">The user's language (messages to them), empty for the tenant default.</param>
public sealed record UserAccount(
    Guid UserId, Guid PersonId, string UserName, string? Email, bool CanSignIn, bool IsActivated, IReadOnlyCollection<TenantRole> Roles,
    string LanguageCode = "");

/// <param name="TemporaryPassword">Set when no link was sent: shown once to the operator, changed at the next sign-in.</param>
public sealed record AccountPasswordReset(string? TemporaryPassword);

/// <summary>
/// The user accounts of the current tenant for the modules that own people (Directory: clients B-01, employees B-02).
/// Writes are Identity operations: inside a running write operation they join its transaction. Sign-in access is
/// independent from the business status of a client (D-05); new accounts have no password (D-06).
/// </summary>
public interface IUserAccounts
{
    /// <summary>Creates the account of an existing person (user name unique, case-insensitive, F01).</summary>
    Task<Result<Guid>> CreateAsync(Guid personId, string userName, string? email, TenantRole role, bool canSignIn, CancellationToken cancellationToken);

    Task<UserAccount?> FindByPersonAsync(Guid personId, CancellationToken cancellationToken);

    /// <summary>The existing accounts among <paramref name="userIds"/>.</summary>
    Task<IReadOnlyList<UserAccount>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Changes user name and e-mail (Q52: the user name stays unique).</summary>
    Task<Result> UpdateAsync(Guid userId, string userName, string? email, CancellationToken cancellationToken);

    Task<Result> SetSignInAsync(Guid userId, bool canSignIn, CancellationToken cancellationToken);

    /// <summary>E-mails a single-use activation link (D-06); fails with a coded error when it cannot leave.</summary>
    Task<Result> SendActivationAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Operator reset: e-mails a reset link, or sets a temporary password returned once.</summary>
    Task<Result<AccountPasswordReset>> ResetPasswordAsync(Guid userId, bool sendLink, CancellationToken cancellationToken);
}
