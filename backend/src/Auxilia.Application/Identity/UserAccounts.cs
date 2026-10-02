using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Identity.Public;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

namespace Auxilia.Application.Identity;

/// <inheritdoc cref="IUserAccounts"/>
internal sealed class UserAccounts(
    IUserAccountManager accounts,
    IAccountLinkManager links,
    IIdentityDataFactory data,
    ITenantContext tenantContext) : IUserAccounts
{
    public Task<Result<Guid>> CreateAsync(Guid personId, string userName, string? email, TenantRole role, bool canSignIn, CancellationToken cancellationToken) =>
        accounts.CreateAsync(
            new CreateUser(personId, userName, email, tenantContext.Current?.DefaultLanguage ?? "it", [role], canSignIn), cancellationToken);

    public async Task<UserAccount?> FindByPersonAsync(Guid personId, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        return await store.FindByPersonAsync(personId, cancellationToken) is { } user ? ToAccount(user) : null;
    }

    public async Task<IReadOnlyList<UserAccount>> FindManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0)
        {
            return [];
        }

        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.FindManyAsync(userIds, cancellationToken)).Select(ToAccount).ToArray();
    }

    public Task<Result> UpdateAsync(Guid userId, string userName, string? email, CancellationToken cancellationToken) =>
        accounts.UpdateAccountAsync(userId, userName, email, cancellationToken);

    public Task<Result> SetSignInAsync(Guid userId, bool canSignIn, CancellationToken cancellationToken) =>
        accounts.SetActiveAsync(userId, canSignIn, cancellationToken);

    public Task<Result> SendActivationAsync(Guid userId, CancellationToken cancellationToken) =>
        links.SendActivationAsync(userId, cancellationToken);

    public async Task<Result<AccountPasswordReset>> ResetPasswordAsync(Guid userId, bool sendLink, CancellationToken cancellationToken)
    {
        string userName;
        await using (var store = await data.OpenAsync(cancellationToken))
        {
            if (await store.FindAsync(userId, cancellationToken) is not { } user)
            {
                return Errors.Identity.UserNotFound();
            }

            userName = user.UserName;
        }

        var reset = await links.ResetPasswordByOperatorAsync(userName, sendLink, cancellationToken);
        return reset.IsFailure
            ? Result.Failure<AccountPasswordReset>(reset.Error!)
            : new AccountPasswordReset(reset.Value.TemporaryPassword);
    }

    private static UserAccount ToAccount(User user) =>
        new(user.Id, user.PersonId, user.UserName, user.Email, user.IsActive, !string.IsNullOrEmpty(user.PasswordHash), user.Roles);
}
