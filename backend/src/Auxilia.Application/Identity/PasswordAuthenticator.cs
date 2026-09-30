using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Results;
using Auxilia.SharedKernel.Tenancy;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Identity;

/// <summary>
/// Sign-in with user name and password (F01), used by the token endpoint (P2-02) through the <c>password</c> login
/// method (P2-07). Every failure returns the same <c>AUX-12002</c> (no user enumeration) except an active lockout
/// (<c>AUX-12003</c>); the reason is only in the security log (<c>AUX-29001</c>).
/// </summary>
public interface IPasswordAuthenticator
{
    Task<Result<AuthenticatedUser>> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);
}

public sealed record AuthenticatedUser(Guid UserId, Guid PersonId, string UserName, string LanguageCode, IReadOnlyCollection<TenantRole> Roles, string SecurityStamp);

internal sealed class PasswordAuthenticator : IPasswordAuthenticator
{
    /// <summary>Verified when the user does not exist, so unknown and known users take the same time.</summary>
    private readonly Lazy<string> dummyHash;

    private readonly IOperationRunner operations;
    private readonly IIdentityDataFactory data;
    private readonly IPasswordHasher hasher;
    private readonly ISettingsProvider settings;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<PasswordAuthenticator> logger;

    public PasswordAuthenticator(
        IOperationRunner operations,
        IIdentityDataFactory data,
        IPasswordHasher hasher,
        ISettingsProvider settings,
        TimeProvider timeProvider,
        ILogger<PasswordAuthenticator> logger)
    {
        this.operations = operations;
        this.data = data;
        this.hasher = hasher;
        this.settings = settings;
        this.timeProvider = timeProvider;
        this.logger = logger;
        dummyHash = new Lazy<string>(() => hasher.Hash(Guid.NewGuid().ToString()));
    }

    public Task<Result<AuthenticatedUser>> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.AuthenticateUser, null, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var user = string.IsNullOrWhiteSpace(userName) ? null : await store.FindByUserNameAsync(userName.Trim(), cancellationToken);
            if (user is null)
            {
                hasher.Verify(dummyHash.Value, PasswordFormat.Identity, password ?? string.Empty);
                return Fail("UnknownUser", null);
            }

            scope.SetEntity("User", user.Id);
            var now = timeProvider.GetUtcNow();
            if (user.IsLockedOut(now))
            {
                Log.Security.LoginFailed(logger, "LockedOut", user.Id);
                return Errors.Identity.AccountLocked();
            }

            var verification = user.PasswordHash is null
                ? PasswordVerification.Failed
                : hasher.Verify(user.PasswordHash, user.PasswordFormat, password ?? string.Empty);

            if (verification == PasswordVerification.Failed)
            {
                var maxAttempts = await settings.GetAsync(IdentitySettings.LockoutMaxFailedAttempts, cancellationToken);
                var lockoutMinutes = await settings.GetAsync(IdentitySettings.LockoutMinutes, cancellationToken);
                var lockedUntil = user.RecordFailedSignIn(now, maxAttempts, TimeSpan.FromMinutes(lockoutMinutes));
                await store.SaveChangesAsync(cancellationToken);
                if (lockedUntil is { } end)
                {
                    Log.Security.AccountLockedOut(logger, user.Id, end, maxAttempts);
                }

                return Fail(user.PasswordHash is null ? "NoPassword" : "WrongPassword", user.Id);
            }

            // D-05: a disabled account cannot sign in, but the correct password does not count as a failed attempt.
            if (!user.IsActive)
            {
                return Fail("Inactive", user.Id);
            }

            if (verification == PasswordVerification.SuccessRehashNeeded)
            {
                var wasLegacy = user.PasswordFormat == PasswordFormat.LegacyBcrypt;
                user.UpgradePasswordHash(hasher.Hash(password!));
                if (wasLegacy)
                {
                    Log.Security.LegacyPasswordUpgraded(logger, user.Id);
                }
            }

            user.RecordSuccessfulSignIn(now);
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success(new AuthenticatedUser(user.Id, user.PersonId, user.UserName, user.LanguageCode, user.Roles, user.SecurityStamp));
        }, cancellationToken);

    private Error Fail(string reason, Guid? userId)
    {
        Log.Security.LoginFailed(logger, reason, userId);
        return Errors.Identity.InvalidCredentials();
    }
}
