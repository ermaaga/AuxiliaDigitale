using Auxilia.Application.Abstractions.Identity;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Application.Identity;

/// <summary>
/// The authenticator app of the users of a tenant (N04): enable it from the profile (QR code confirmed by a code),
/// disable it with the password, reset it (Administrator or platform, lost phone). A reset ends the user's sessions.
/// </summary>
public interface ITwoFactorManager
{
    Task<Result<TwoFactorStatus>> StatusAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>A new secret for the app (shown once); replaces an enrolment not yet confirmed.</summary>
    Task<Result<TotpEnrollment>> BeginEnrollmentAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result> ConfirmEnrollmentAsync(Guid userId, string code, CancellationToken cancellationToken);

    /// <summary>The user removes their own app with the current password; refused when their roles require it.</summary>
    Task<Result> DisableAsync(Guid userId, string password, CancellationToken cancellationToken);

    /// <summary>An Administrator removes the app of a user (lost phone): the user's sessions end.</summary>
    Task<Result> ResetAsync(Guid userId, Guid? actorUserId, CancellationToken cancellationToken);

    /// <summary>The platform staff (<c>auxctl users reset-mfa</c>) removes the app of a user, found by user name.</summary>
    Task<Result<string>> ResetByOperatorAsync(string userName, CancellationToken cancellationToken);
}

/// <param name="Required">The user's roles require the app (<c>auth.mfa.requiredRoles</c>): it cannot be disabled.</param>
public sealed record TwoFactorStatus(bool Enabled, bool Required, DateTimeOffset? EnabledAt);

internal sealed partial class SessionManager : ITwoFactorManager
{
    public async Task<Result<TwoFactorStatus>> StatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        if (await store.FindUserAsync(userId, cancellationToken) is not { IsActive: true } user)
        {
            return Errors.Identity.UserNotFound();
        }

        return Result.Success(new TwoFactorStatus(user.HasTwoFactor, await IsTwoFactorRequiredAsync(user, cancellationToken), user.TwoFactorEnabledAt));
    }

    public Task<Result<TotpEnrollment>> BeginEnrollmentAsync(Guid userId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.BeginTwoFactorEnrollment, new { UserId = userId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindUserAsync(userId, cancellationToken) is not { IsActive: true } user)
            {
                return Errors.Identity.UserNotFound();
            }

            scope.SetEntity("User", user.Id);
            var enrollment = await BeginAsync(user, cancellationToken);
            await SaveAsync(store, cancellationToken);
            return enrollment;
        }, cancellationToken);

    public Task<Result> ConfirmEnrollmentAsync(Guid userId, string code, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.EnableTwoFactor, new { UserId = userId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindUserAsync(userId, cancellationToken) is not { IsActive: true } user)
            {
                return Errors.Identity.UserNotFound();
            }

            scope.SetEntity("User", user.Id);
            var confirmed = Confirm(user, code, timeProvider.GetUtcNow());
            if (confirmed.IsSuccess)
            {
                Log.Security.TwoFactorChanged(logger, user.Id, "Enabled", "User", user.Id);
                await SaveAsync(store, cancellationToken);
            }

            return confirmed;
        }, cancellationToken);

    public Task<Result> DisableAsync(Guid userId, string password, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.RemoveTwoFactor, new { UserId = userId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindUserAsync(userId, cancellationToken) is not { IsActive: true } user)
            {
                return Errors.Identity.UserNotFound();
            }

            scope.SetEntity("User", user.Id);
            var now = timeProvider.GetUtcNow();
            var passwordOk = user.PasswordHash is { } hash
                && hasher.Verify(hash, user.PasswordFormat, password ?? string.Empty) != PasswordVerification.Failed;
            if (!passwordOk)
            {
                // Guessing the password through this endpoint counts toward the lockout like a sign-in.
                var maxAttempts = await settings.GetAsync(IdentitySettings.LockoutMaxFailedAttempts, cancellationToken);
                var lockoutMinutes = await settings.GetAsync(IdentitySettings.LockoutMinutes, cancellationToken);
                if (user.RecordFailedSignIn(now, maxAttempts, TimeSpan.FromMinutes(lockoutMinutes)) is { } end)
                {
                    Log.Security.AccountLockedOut(logger, user.Id, end, maxAttempts);
                }

                await SaveAsync(store, cancellationToken);
                return Errors.Identity.CurrentPasswordInvalid();
            }

            if (await IsTwoFactorRequiredAsync(user, cancellationToken))
            {
                return Errors.Identity.TwoFactorRequiredByRole();
            }

            user.RemoveTwoFactor(endSessions: false);
            Log.Security.TwoFactorChanged(logger, user.Id, "Disabled", "User", user.Id);
            await SaveAsync(store, cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> ResetAsync(Guid userId, Guid? actorUserId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.RemoveTwoFactor, new { UserId = userId }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindUserAsync(userId, cancellationToken) is not { } user)
            {
                return Errors.Identity.UserNotFound();
            }

            scope.SetEntity("User", user.Id);
            await ResetAsync(store, user, "Administrator", actorUserId, cancellationToken);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<string>> ResetByOperatorAsync(string userName, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.RemoveTwoFactor, null, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(userName) || await store.FindUserByUserNameAsync(userName.Trim(), cancellationToken) is not { } user)
            {
                return Errors.Identity.UserNotFound();
            }

            scope.SetEntity("User", user.Id);
            await ResetAsync(store, user, "Operator", null, cancellationToken);
            return Result.Success(user.UserName);
        }, cancellationToken);

    public Task<Result<TotpEnrollment>> BeginRequiredSetupAsync(TwoFactorSetup request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Identity.BeginTwoFactorEnrollment, new { request.Client.ClientId }, async scope =>
        {
            var setup = await RequiredSetupUserAsync(request.Client, request.UserName, request.Password, request.IpAddress, request.UserAgent, cancellationToken);
            if (setup.IsFailure)
            {
                return Result.Failure<TotpEnrollment>(setup.Error!);
            }

            await using var store = setup.Value.Store;
            scope.SetEntity("User", setup.Value.User.Id);
            var enrollment = await BeginAsync(setup.Value.User, cancellationToken);
            await SaveAsync(store, cancellationToken);
            return enrollment;
        }, cancellationToken);
    }

    public Task<Result<TokenPair>> ConfirmRequiredSetupAsync(TwoFactorSetupConfirmation request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync(Operations.Identity.EnableTwoFactor, new { request.Client.ClientId }, async scope =>
        {
            var setup = await RequiredSetupUserAsync(request.Client, request.UserName, request.Password, request.IpAddress, request.UserAgent, cancellationToken);
            if (setup.IsFailure)
            {
                return Result.Failure<TokenPair>(setup.Error!);
            }

            await using var store = setup.Value.Store;
            var user = setup.Value.User;
            scope.SetEntity("User", user.Id);
            var now = timeProvider.GetUtcNow();
            var attempt = new AttemptInfo(request.UserName, LoginMethods.Password, request.IpAddress, request.UserAgent);
            var confirmed = Confirm(user, request.Code, now);
            if (confirmed.IsFailure)
            {
                return await FailAsync(store, attempt, user.Id, "TwoFactorCodeRejected", confirmed.Error!, cancellationToken);
            }

            Log.Security.TwoFactorChanged(logger, user.Id, "Enabled", "User", user.Id);
            return Result.Success(await OpenSessionAsync(store, scope, user, request.Client, attempt, request.RememberMe, now, cancellationToken));
        }, cancellationToken);
    }

    /// <summary>
    /// The second factor of a sign-in (N04), after the password: null when the sign-in may go on, otherwise the recorded
    /// failure. Users with the app must send its code; users whose roles require it and have none must enrol it first.
    /// </summary>
    private async Task<Result<TokenPair>?> CheckSecondFactorAsync(
        ISessionData store, User user, string? code, AttemptInfo attempt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (user.HasTwoFactor)
        {
            return await VerifyCodeAsync(store, user, code, attempt, now, cancellationToken);
        }

        return await IsTwoFactorRequiredAsync(user, cancellationToken)
            ? await FailAsync(store, attempt, user.Id, "TwoFactorSetupRequired", Errors.Identity.TwoFactorSetupRequired(), cancellationToken)
            : null;
    }

    /// <summary>
    /// The code of the user's app: none asks for it (not a failed attempt); a wrong, expired or replayed one counts as a
    /// failed sign-in (progressive lockout, F35); a right one resets the counter the password left untouched.
    /// </summary>
    private async Task<Result<TokenPair>?> VerifyCodeAsync(
        ISessionData store, User user, string? code, AttemptInfo attempt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return await FailAsync(store, attempt, user.Id, "TwoFactorRequired", Errors.Identity.TwoFactorRequired(), cancellationToken);
        }

        var step = totp.Verify(secretProtector.Unprotect(user.TwoFactorSecret!), code.Trim(), now);
        if (step is { } matched && user.TryUseTotpStep(matched))
        {
            user.RecordSuccessfulSignIn(now);
            return null;
        }

        var maxAttempts = await settings.GetAsync(IdentitySettings.LockoutMaxFailedAttempts, cancellationToken);
        var lockoutMinutes = await settings.GetAsync(IdentitySettings.LockoutMinutes, cancellationToken);
        if (user.RecordFailedSignIn(now, maxAttempts, TimeSpan.FromMinutes(lockoutMinutes)) is { } end)
        {
            Log.Security.AccountLockedOut(logger, user.Id, end, maxAttempts);
        }

        Log.Security.TwoFactorCodeFailed(logger, user.Id, step is null ? "WrongCode" : "Replayed");
        return await FailAsync(store, attempt, user.Id, "TwoFactorCodeRejected", Errors.Identity.TwoFactorCodeRejected(), cancellationToken);
    }

    private async Task<bool> IsTwoFactorRequiredAsync(User user, CancellationToken cancellationToken)
    {
        var required = IdentitySettings.ParseRoles(await settings.GetAsync(IdentitySettings.MfaRequiredRoles, cancellationToken));
        return required is { Count: > 0 } && user.Roles.Any(required.Contains);
    }

    /// <summary>The idle window of a "stay signed in" session, or null when the tenant turned the box off (0 days).</summary>
    private async Task<TimeSpan?> RememberWindowAsync(CancellationToken cancellationToken) =>
        await settings.GetAsync(IdentitySettings.RememberMeDays, cancellationToken) is var days and > 0 ? TimeSpan.FromDays(days) : null;

    private async Task<Result<TotpEnrollment>> BeginAsync(User user, CancellationToken cancellationToken)
    {
        if (user.HasTwoFactor)
        {
            return Errors.Identity.TwoFactorAlreadyEnabled();
        }

        var secret = totp.NewSecret();
        user.BeginTwoFactorEnrollment(secretProtector.Protect(secret));
        return Result.Success(new TotpEnrollment(secret, totp.EnrollmentUri(secret, user.UserName, await appName.GetAsync(cancellationToken))));
    }

    private Result Confirm(User user, string? code, DateTimeOffset now)
    {
        if (user.HasTwoFactor)
        {
            return Errors.Identity.TwoFactorAlreadyEnabled();
        }

        if (user.PendingTwoFactorSecret is not { } pending)
        {
            return Errors.Identity.TwoFactorEnrollmentMissing();
        }

        return string.IsNullOrWhiteSpace(code) || totp.Verify(secretProtector.Unprotect(pending), code.Trim(), now) is not { } step
            ? Errors.Identity.TwoFactorCodeRejected()
            : user.ConfirmTwoFactor(step, now);
    }

    /// <summary>Removes the app and ends every open session of the user (new security stamp), then saves.</summary>
    private async Task ResetAsync(ISessionData store, User user, string actorType, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        user.RemoveTwoFactor(endSessions: true);
        foreach (var session in await store.OpenSessionsOfUserAsync(user.Id, cancellationToken))
        {
            await EndAsync(session, SessionEndReason.SecurityStampChanged, now, cancellationToken);
        }

        Log.Security.TwoFactorChanged(logger, user.Id, "Reset", actorType, actorUserId);
        await SaveAsync(store, cancellationToken);
    }

    /// <summary>
    /// The credential of the public setup page: client, user name and password (failures recorded like a sign-in), and
    /// only for a user whose roles require the app and who has none. The caller disposes the store.
    /// </summary>
    private async Task<Result<(ISessionData Store, User User)>> RequiredSetupUserAsync(
        ClientCredentials client, string userName, string password, string? ipAddress, string? userAgent, CancellationToken cancellationToken)
    {
        var store = await data.OpenAsync(cancellationToken);
        try
        {
            var attempt = new AttemptInfo(userName, LoginMethods.Password, ipAddress, userAgent);
            if (!await clients.ValidateAsync(client, cancellationToken))
            {
                return Result.Failure<(ISessionData, User)>((await FailAsync(store, attempt, null, "ClientInvalid", Errors.Identity.ClientInvalid(), cancellationToken)).Error!);
            }

            var authenticated = await authenticator.AuthenticateAsync(userName, password, cancellationToken);
            if (authenticated.IsFailure)
            {
                var userId = string.IsNullOrWhiteSpace(userName) ? null : (await store.FindUserByUserNameAsync(userName.Trim(), cancellationToken))?.Id;
                var reason = authenticated.Error!.Code == EventCodes.Identity.AccountLocked ? "LockedOut" : "InvalidCredentials";
                return Result.Failure<(ISessionData, User)>((await FailAsync(store, attempt, userId, reason, authenticated.Error, cancellationToken)).Error!);
            }

            if (await store.FindUserAsync(authenticated.Value.UserId, cancellationToken) is not { } user)
            {
                return Result.Failure<(ISessionData, User)>(Errors.Identity.InvalidCredentials());
            }

            if (user.HasTwoFactor)
            {
                return Result.Failure<(ISessionData, User)>(Errors.Identity.TwoFactorAlreadyEnabled());
            }

            if (!await IsTwoFactorRequiredAsync(user, cancellationToken))
            {
                return Result.Failure<(ISessionData, User)>(Errors.Identity.TwoFactorEnrollmentMissing());
            }

            var keep = store;
            store = null;
            return Result.Success((keep, user));
        }
        finally
        {
            if (store is not null)
            {
                await store.DisposeAsync();
            }
        }
    }
}
