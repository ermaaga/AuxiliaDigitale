using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Platform;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Identity;

/// <summary>
/// Rules of platform (System) identity (N02, D-22): fixed by code, not tenant settings. Platform users are few and
/// technical; their tokens never grant business data (D-21).
/// </summary>
public static class PlatformIdentityPolicy
{
    public const string TotpIssuer = "Auxilia";

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(10);

    /// <summary>A tenant opened from the console: short, re-issued on demand.</summary>
    public static readonly TimeSpan TenantTokenLifetime = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan SessionIdle = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan SessionAbsolute = TimeSpan.FromHours(12);

    public static readonly TimeSpan ActivationTokenLifetime = TimeSpan.FromHours(72);

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public const int LockoutMaxFailedAttempts = 5;

    public const int PasswordMinLength = 12;
}

/// <summary>Platform users (<c>auxctl platform users …</c>): creation, credential reset, enable/disable.</summary>
public interface IPlatformUserManager
{
    /// <returns>The user and its one-use activation token (shown once; only its hash is stored).</returns>
    Task<Result<PlatformUserActivation>> AddAsync(string email, string displayName, CancellationToken cancellationToken);

    /// <summary>Clears password and TOTP, ends every console session and issues a new activation token.</summary>
    Task<Result<PlatformUserActivation>> ResetAsync(string email, CancellationToken cancellationToken);

    Task<Result> SetActiveAsync(string email, bool isActive, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlatformUser>> ListAsync(CancellationToken cancellationToken);
}

public sealed record PlatformUserActivation(PlatformUser User, string ActivationToken, DateTimeOffset ExpiresAt);

internal sealed class PlatformUserManager : IPlatformUserManager
{
    private readonly IOperationRunner operations;
    private readonly IPlatformIdentityStore store;
    private readonly IPlatformSessionEnder sessions;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<PlatformUserManager> logger;

    public PlatformUserManager(
        IOperationRunner operations, IPlatformIdentityStore store, IPlatformSessionEnder sessions, TimeProvider timeProvider, ILogger<PlatformUserManager> logger)
    {
        this.operations = operations;
        this.store = store;
        this.sessions = sessions;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public Task<Result<PlatformUserActivation>> AddAsync(string email, string displayName, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.CreatePlatformUser, null, async scope =>
        {
            if (!IsValid(email, PlatformUser.EmailMaxLength) || !email.Contains('@', StringComparison.Ordinal))
            {
                return Errors.Identity.UserValueInvalid("email");
            }

            if (!IsValid(displayName, PlatformUser.DisplayNameMaxLength))
            {
                return Errors.Identity.UserValueInvalid("displayName");
            }

            if (await store.FindUserByEmailAsync(email.Trim(), cancellationToken) is not null)
            {
                return Errors.Identity.PlatformUserEmailTaken();
            }

            var user = new PlatformUser(Guid.CreateVersion7(), email, displayName);
            store.Add(user);
            scope.SetEntity("PlatformUser", user.Id);
            var activation = await IssueActivationAsync(user, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success(activation);
        }, cancellationToken);

    public Task<Result<PlatformUserActivation>> ResetAsync(string email, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.ResetPlatformCredentials, null, async scope =>
        {
            if (string.IsNullOrWhiteSpace(email) || await store.FindUserByEmailAsync(email.Trim(), cancellationToken) is not { } user)
            {
                return Errors.Identity.UserNotFound();
            }

            scope.SetEntity("PlatformUser", user.Id);
            user.ResetCredentials();
            var activation = await IssueActivationAsync(user, cancellationToken);
            await sessions.EndAllAsync(user.Id, SessionEndReason.SecurityStampChanged, cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            Log.Security.PlatformCredentialsChanged(logger, user.Id, "reset");
            return Result.Success(activation);
        }, cancellationToken);

    public Task<Result> SetActiveAsync(string email, bool isActive, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.SetPlatformUserActive, null, async scope =>
        {
            if (string.IsNullOrWhiteSpace(email) || await store.FindUserByEmailAsync(email.Trim(), cancellationToken) is not { } user)
            {
                return Errors.Identity.UserNotFound();
            }

            scope.SetEntity("PlatformUser", user.Id);
            if (isActive)
            {
                user.Activate();
            }
            else
            {
                user.Deactivate();
                await sessions.EndAllAsync(user.Id, SessionEndReason.Revoked, cancellationToken);
            }

            await store.SaveChangesAsync(cancellationToken);
            Log.Security.AccountActivationChanged(logger, user.Id, isActive);
            return Result.Success();
        }, cancellationToken);

    public Task<IReadOnlyList<PlatformUser>> ListAsync(CancellationToken cancellationToken) => store.ListUsersAsync(cancellationToken);

    private async Task<PlatformUserActivation> IssueActivationAsync(PlatformUser user, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var previous in await store.UnusedUserTokensAsync(user.Id, UserTokenPurpose.Activation, cancellationToken))
        {
            previous.Use(now);
        }

        var token = SecureTokens.New();
        var expiresAt = now + PlatformIdentityPolicy.ActivationTokenLifetime;
        store.Add(new UserToken(Guid.CreateVersion7(), user.Id, UserTokenPurpose.Activation, SecureTokens.Hash(token), now, expiresAt));
        return new PlatformUserActivation(user, token, expiresAt);
    }

    private static bool IsValid(string? value, int maxLength) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength;
}
