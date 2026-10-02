using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Identity;

/// <summary>
/// User accounts of the current tenant (F01, D-05, D-06). Used by the Directory module when it creates clients and
/// employees (B-01, B-02), by activation and password reset (P2-02) and by auxctl (P2-07). Authorization comes with
/// P2-03.
/// </summary>
public interface IUserAccountManager
{
    /// <summary>Creates the account of an existing person, without password (activation link, D-06).</summary>
    Task<Result<Guid>> CreateAsync(CreateUser request, CancellationToken cancellationToken);

    /// <summary>Sets a new password after checking the policy; unlocks the account and invalidates older sessions.</summary>
    Task<Result> SetPasswordAsync(Guid userId, string password, CancellationToken cancellationToken);

    /// <summary>Enables or disables sign-in (independent from the client status, D-05).</summary>
    Task<Result> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);

    Task<Result> SetRolesAsync(Guid userId, IReadOnlyCollection<TenantRole> roles, CancellationToken cancellationToken);

    /// <summary>Changes user name and e-mail; the user name stays unique (Q52).</summary>
    Task<Result> UpdateAccountAsync(Guid userId, string userName, string? email, CancellationToken cancellationToken);
}

/// <param name="UserName">Unique, case-insensitive (for clients it is the e-mail, F05).</param>
public sealed record CreateUser(Guid PersonId, string UserName, string? Email, string LanguageCode, IReadOnlyCollection<TenantRole> Roles, bool IsActive);

internal sealed class UserAccountManager : IUserAccountManager
{
    private readonly IOperationRunner operations;
    private readonly IIdentityDataFactory data;
    private readonly IPasswordHasher hasher;
    private readonly IPasswordPolicy passwordPolicy;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<UserAccountManager> logger;

    public UserAccountManager(
        IOperationRunner operations,
        IIdentityDataFactory data,
        IPasswordHasher hasher,
        IPasswordPolicy passwordPolicy,
        TimeProvider timeProvider,
        ILogger<UserAccountManager> logger)
    {
        this.operations = operations;
        this.data = data;
        this.hasher = hasher;
        this.passwordPolicy = passwordPolicy;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public Task<Result<Guid>> CreateAsync(CreateUser request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Identity.CreateUser, new { request.PersonId }, async scope =>
        {
            var created = User.Create(Guid.CreateVersion7(), request.PersonId, request.UserName, request.Email, request.LanguageCode, request.Roles, request.IsActive);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error!);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (!await store.PersonExistsAsync(request.PersonId, cancellationToken))
            {
                return Errors.Identity.PersonNotFound();
            }

            if (await store.UserNameExistsAsync(created.Value.UserName, cancellationToken))
            {
                return Errors.Identity.UserNameTaken();
            }

            store.Add(created.Value);
            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("User", created.Value.Id);
            return Result.Success(created.Value.Id);
        }, cancellationToken);
    }

    public Task<Result> SetPasswordAsync(Guid userId, string password, CancellationToken cancellationToken) =>
        ChangeAsync(Operations.Identity.SetPassword, userId, async user =>
        {
            // Staff-set passwords follow the same policy and history (F35).
            var valid = await passwordPolicy.ValidateAsync(user, password, cancellationToken);
            if (valid.IsFailure)
            {
                return valid;
            }

            user.SetPassword(hasher.Hash(password), PasswordFormat.Identity, timeProvider.GetUtcNow());
            Log.Security.PasswordChanged(logger, user.Id);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken) =>
        ChangeAsync(Operations.Identity.SetUserActive, userId, user =>
        {
            user.SetActive(isActive);
            Log.Security.AccountActivationChanged(logger, user.Id, isActive);
            return Task.FromResult(Result.Success());
        }, cancellationToken);

    public Task<Result> SetRolesAsync(Guid userId, IReadOnlyCollection<TenantRole> roles, CancellationToken cancellationToken) =>
        ChangeAsync(Operations.Identity.SetUserRoles, userId, user =>
        {
            var result = user.SetRoles(roles);
            if (result.IsSuccess)
            {
                Log.Security.RolesChanged(logger, user.Id, roles);
            }

            return Task.FromResult(result);
        }, cancellationToken);

    public Task<Result> UpdateAccountAsync(Guid userId, string userName, string? email, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.UpdateUserAccount, new { UserId = userId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(userId, cancellationToken) is not { } user)
            {
                return Errors.Identity.UserNotFound();
            }

            var name = userName?.Trim() ?? string.Empty;
            if (!string.Equals(user.UserName, name, StringComparison.OrdinalIgnoreCase) && await store.UserNameExistsAsync(name, cancellationToken))
            {
                return Errors.Identity.UserNameTaken();
            }

            var changed = user.ChangeAccount(name, email);
            if (changed.IsSuccess)
            {
                await store.SaveChangesAsync(cancellationToken);
            }

            return changed;
        }, cancellationToken);

    private Task<Result> ChangeAsync(OperationDescriptor operation, Guid userId, Func<User, Task<Result>> change, CancellationToken cancellationToken) =>
        operations.RunAsync(operation, new { UserId = userId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(userId, cancellationToken) is not { } user)
            {
                return Errors.Identity.UserNotFound();
            }

            var result = await change(user);
            if (result.IsSuccess)
            {
                await store.SaveChangesAsync(cancellationToken);
            }

            return result;
        }, cancellationToken);
}
