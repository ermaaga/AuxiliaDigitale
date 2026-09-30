using Auxilia.Application.Abstractions.Identity;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Identity;

/// <summary>Account security (F35): sign-in with an e-mailed code, expired and self-service password changes.</summary>
internal sealed partial class SessionManager
{
    public Task<Result<TokenPair>> SignInWithOtpAsync(OtpSignIn request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Identity.SignInWithOtp, new { request.Client.ClientId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var attempt = new AttemptInfo(request.UserName, LoginMethods.EmailOtp, request.IpAddress, request.UserAgent);
            if (!await settings.GetAsync(IdentitySettings.OtpLoginEnabled, cancellationToken))
            {
                return Errors.Identity.LoginMethodDisabled(LoginMethods.EmailOtp);
            }

            if (!await clients.ValidateAsync(request.Client, cancellationToken))
            {
                return await FailAsync(store, attempt, null, "ClientInvalid", Errors.Identity.ClientInvalid(), cancellationToken);
            }

            var user = string.IsNullOrWhiteSpace(request.UserName) ? null : await store.FindUserByUserNameAsync(request.UserName.Trim(), cancellationToken);
            if (user is null || !user.IsActive)
            {
                Log.Security.LoginFailed(logger, user is null ? "UnknownUser" : "Inactive", user?.Id);
                return await FailAsync(store, attempt, user?.Id, "InvalidOtp", Errors.Identity.InvalidCredentials(), cancellationToken);
            }

            scope.SetEntity("User", user.Id);
            var now = timeProvider.GetUtcNow();
            if (user.IsLockedOut(now))
            {
                Log.Security.LoginFailed(logger, "LockedOut", user.Id);
                return await FailAsync(store, attempt, user.Id, "LockedOut", Errors.Identity.AccountLocked(), cancellationToken);
            }

            var token = string.IsNullOrWhiteSpace(request.Code)
                ? null
                : await store.FindUserTokenAsync(LoginOtpHash(user.Id, request.Code.Trim()), UserTokenPurpose.LoginOtp, cancellationToken);
            if (token is null || !token.IsUsableAt(now))
            {
                // A wrong code counts as a failed sign-in: the lockout is the attempt limit, and it voids the pending codes.
                var maxAttempts = await settings.GetAsync(IdentitySettings.LockoutMaxFailedAttempts, cancellationToken);
                var lockoutMinutes = await settings.GetAsync(IdentitySettings.LockoutMinutes, cancellationToken);
                if (user.RecordFailedSignIn(now, maxAttempts, TimeSpan.FromMinutes(lockoutMinutes)) is { } end)
                {
                    Log.Security.AccountLockedOut(logger, user.Id, end, maxAttempts);
                    foreach (var pending in await store.UnusedUserTokensAsync(user.Id, UserTokenPurpose.LoginOtp, cancellationToken))
                    {
                        pending.Use(now);
                    }
                }

                Log.Security.LoginFailed(logger, "InvalidOtp", user.Id);
                return await FailAsync(store, attempt, user.Id, "InvalidOtp", Errors.Identity.InvalidCredentials(), cancellationToken);
            }

            token.Use(now);
            user.RecordSuccessfulSignIn(now);
            return Result.Success(await OpenSessionAsync(store, scope, user, request.Client, attempt, now, cancellationToken));
        }, cancellationToken);
    }

    public Task<Result<TokenPair>> ChangeExpiredPasswordAsync(ExpiredPasswordChange request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Identity.ChangeExpiredPassword, new { request.Client.ClientId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var attempt = new AttemptInfo(request.UserName, LoginMethods.Password, request.IpAddress, request.UserAgent);
            if (!await clients.ValidateAsync(request.Client, cancellationToken))
            {
                return await FailAsync(store, attempt, null, "ClientInvalid", Errors.Identity.ClientInvalid(), cancellationToken);
            }

            var authenticated = await authenticator.AuthenticateAsync(request.UserName, request.CurrentPassword, cancellationToken);
            if (authenticated.IsFailure)
            {
                var userId = string.IsNullOrWhiteSpace(request.UserName) ? null : (await store.FindUserByUserNameAsync(request.UserName.Trim(), cancellationToken))?.Id;
                var reason = authenticated.Error!.Code == EventCodes.Identity.AccountLocked ? "LockedOut" : "InvalidCredentials";
                return await FailAsync(store, attempt, userId, reason, authenticated.Error, cancellationToken);
            }

            scope.SetEntity("User", authenticated.Value.UserId);
            if (await store.FindUserAsync(authenticated.Value.UserId, cancellationToken) is not { } user)
            {
                return Errors.Identity.InvalidCredentials();
            }

            var now = timeProvider.GetUtcNow();
            var changed = await SetNewPasswordAsync(store, user, request.NewPassword, keepSessionId: null, now, cancellationToken);
            if (changed.IsFailure)
            {
                return Result.Failure<TokenPair>(changed.Error!);
            }

            return Result.Success(await OpenSessionAsync(store, scope, user, request.Client, attempt, now, cancellationToken));
        }, cancellationToken);
    }

    public Task<Result> ChangePasswordAsync(Guid userId, Guid? currentSessionId, string currentPassword, string newPassword, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.ChangePassword, new { UserId = userId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindUserAsync(userId, cancellationToken) is not { IsActive: true } user)
            {
                return Errors.Identity.UserNotFound();
            }

            scope.SetEntity("User", user.Id);
            var now = timeProvider.GetUtcNow();
            var currentOk = user.PasswordHash is { } hash
                && hasher.Verify(hash, user.PasswordFormat, currentPassword ?? string.Empty) != PasswordVerification.Failed;
            if (!currentOk)
            {
                // Guessing the current password through this endpoint counts toward the lockout like a sign-in.
                var maxAttempts = await settings.GetAsync(IdentitySettings.LockoutMaxFailedAttempts, cancellationToken);
                var lockoutMinutes = await settings.GetAsync(IdentitySettings.LockoutMinutes, cancellationToken);
                if (user.RecordFailedSignIn(now, maxAttempts, TimeSpan.FromMinutes(lockoutMinutes)) is { } end)
                {
                    Log.Security.AccountLockedOut(logger, user.Id, end, maxAttempts);
                }

                await SaveAsync(store, cancellationToken);
                return Errors.Identity.CurrentPasswordInvalid();
            }

            var changed = await SetNewPasswordAsync(store, user, newPassword, currentSessionId, now, cancellationToken);
            if (changed.IsSuccess)
            {
                await SaveAsync(store, cancellationToken);
            }

            return changed;
        }, cancellationToken);

    /// <summary>
    /// Policy and history, then the new hash; every open session of the user ends except <paramref name="keepSessionId"/>,
    /// which moves to the new security stamp. The caller saves.
    /// </summary>
    private async Task<Result> SetNewPasswordAsync(
        ISessionData store, User user, string newPassword, Guid? keepSessionId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var valid = await passwordPolicy.ValidateAsync(user, newPassword, cancellationToken);
        if (valid.IsFailure)
        {
            return valid;
        }

        user.SetPassword(hasher.Hash(newPassword), PasswordFormat.Identity, now);
        foreach (var session in await store.OpenSessionsOfUserAsync(user.Id, cancellationToken))
        {
            if (session.Id == keepSessionId)
            {
                session.RenewSecurityStamp(user.SecurityStamp);
            }
            else
            {
                await EndAsync(session, SessionEndReason.SecurityStampChanged, now, cancellationToken);
            }
        }

        Log.Security.PasswordChanged(logger, user.Id);
        return Result.Success();
    }

    /// <summary>E-mailed sign-in codes are 6 digits: the user id in the hash keeps them unique per user.</summary>
    internal static string LoginOtpHash(Guid userId, string code) => SecureTokens.Hash($"{userId:N}:{code}");
}
