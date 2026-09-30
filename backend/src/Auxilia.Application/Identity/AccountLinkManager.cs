using Auxilia.Application.Abstractions.Identity;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Application.Abstractions.Tenancy;
using Auxilia.Application.Messaging;
using Auxilia.Application.Messaging.Public;
using Auxilia.Diagnostics;
using Auxilia.Domain.Identity;
using Auxilia.Domain.Messaging;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Identity;

/// <summary>
/// E-mail links (D-06, F01): single-use activation link for new accounts (sent by staff; a new link invalidates the
/// previous ones) and password reset (requested by the user; the answer never tells whether the account exists).
/// Links point to <c>{auth.appBaseUrl}/{tenant}/activate|reset-password?token=…</c> and store only the token hash.
/// </summary>
public interface IAccountLinkManager
{
    Task<Result> SendActivationAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result> ActivateAsync(string token, string password, CancellationToken cancellationToken);

    /// <summary>Always succeeds (no user enumeration); sends the link only to an active account with an e-mail.</summary>
    Task<Result> RequestPasswordResetAsync(string userName, CancellationToken cancellationToken);

    /// <summary>Sets the new password and ends every session of the user.</summary>
    Task<Result> ResetPasswordAsync(string token, string password, CancellationToken cancellationToken);

    /// <summary>
    /// E-mails a 6-digit sign-in code (method <c>email-otp</c>, F35) when the method is enabled; succeeds whether or not
    /// the user exists (no enumeration). A new code voids the previous ones.
    /// </summary>
    Task<Result> SendLoginOtpAsync(string userName, CancellationToken cancellationToken);

    /// <summary>
    /// Operator reset (auxctl, F31) of the user found by user name or, failing that, by e-mail: either e-mails a reset
    /// link, or sets a random temporary password (returned once) that must be changed at the next sign-in; the
    /// temporary password ends every session of the user.
    /// </summary>
    Task<Result<OperatorPasswordReset>> ResetPasswordByOperatorAsync(string userNameOrEmail, bool sendLink, CancellationToken cancellationToken);
}

/// <param name="TemporaryPassword">Only without the link; shown once, never stored in clear.</param>
public sealed record OperatorPasswordReset(Guid UserId, string UserName, string? TemporaryPassword);

internal sealed class AccountLinkManager : IAccountLinkManager
{
    private readonly IOperationRunner operations;
    private readonly ISessionDataFactory data;
    private readonly IPasswordHasher hasher;
    private readonly IMessageDispatcher messages;
    private readonly ISessionManager sessions;
    private readonly ISettingsProvider settings;
    private readonly IPasswordPolicy passwordPolicy;
    private readonly ITenantContext tenantContext;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<AccountLinkManager> logger;

    public AccountLinkManager(
        IOperationRunner operations,
        ISessionDataFactory data,
        IPasswordHasher hasher,
        IMessageDispatcher messages,
        ISessionManager sessions,
        ISettingsProvider settings,
        IPasswordPolicy passwordPolicy,
        ITenantContext tenantContext,
        TimeProvider timeProvider,
        ILogger<AccountLinkManager> logger)
    {
        this.operations = operations;
        this.data = data;
        this.hasher = hasher;
        this.messages = messages;
        this.sessions = sessions;
        this.settings = settings;
        this.passwordPolicy = passwordPolicy;
        this.tenantContext = tenantContext;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    public Task<Result> SendActivationAsync(Guid userId, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.SendActivation, new { UserId = userId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindUserAsync(userId, cancellationToken) is not { } user)
            {
                return Errors.Identity.UserNotFound();
            }

            if (user.Email is null)
            {
                return Errors.Identity.UserEmailMissing();
            }

            var hours = await settings.GetAsync(IdentitySettings.ActivationLinkHours, cancellationToken);
            return await SendLinkAsync(store, user, UserTokenPurpose.Activation, TimeSpan.FromHours(hours), "activate", MessageTemplates.AccountActivation, cancellationToken);
        }, cancellationToken);

    public Task<Result> ActivateAsync(string token, string password, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.ActivateAccount, null, async scope =>
        {
            var used = await UseTokenAsync(token, UserTokenPurpose.Activation, password, scope, cancellationToken);
            if (used.IsSuccess)
            {
                Log.Security.AccountActivated(logger, used.Value);
            }

            return (Result)used;
        }, cancellationToken);

    public Task<Result> RequestPasswordResetAsync(string userName, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.RequestPasswordReset, null, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var user = string.IsNullOrWhiteSpace(userName) ? null : await store.FindUserByUserNameAsync(userName.Trim(), cancellationToken);
            Log.Security.PasswordResetRequested(logger, user?.Id);
            if (user is null || !user.IsActive || user.Email is null || user.PasswordHash is null)
            {
                return Result.Success();
            }

            var minutes = await settings.GetAsync(IdentitySettings.PasswordResetLinkMinutes, cancellationToken);
            var sent = await SendLinkAsync(store, user, UserTokenPurpose.PasswordReset, TimeSpan.FromMinutes(minutes), "reset-password", MessageTemplates.PasswordReset, cancellationToken);

            // Still no enumeration: a delivery problem is logged by the dispatcher, the caller sees success.
            return sent.IsSuccess ? sent : Result.Success();
        }, cancellationToken);

    public Task<Result> ResetPasswordAsync(string token, string password, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.ResetPassword, null, async scope =>
        {
            var used = await UseTokenAsync(token, UserTokenPurpose.PasswordReset, password, scope, cancellationToken);
            if (used.IsFailure)
            {
                return (Result)used;
            }

            await using (var store = await data.OpenAsync(cancellationToken))
            {
                foreach (var session in await store.OpenSessionsOfUserAsync(used.Value, cancellationToken))
                {
                    var ended = await sessions.EndSessionAsync(session.Id, SessionEndReason.SecurityStampChanged, cancellationToken);
                    if (ended.IsFailure)
                    {
                        return ended;
                    }
                }
            }

            Log.Security.PasswordResetCompleted(logger, used.Value);
            return Result.Success();
        }, cancellationToken);

    public Task<Result> SendLoginOtpAsync(string userName, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.RequestLoginOtp, null, async scope =>
        {
            if (!await settings.GetAsync(IdentitySettings.OtpLoginEnabled, cancellationToken))
            {
                return Errors.Identity.LoginMethodDisabled(LoginMethods.EmailOtp);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            var user = string.IsNullOrWhiteSpace(userName) ? null : await store.FindUserByUserNameAsync(userName.Trim(), cancellationToken);
            if (user is null || !user.IsActive || user.Email is null || user.IsLockedOut(now))
            {
                return Result.Success();
            }

            scope.SetEntity("User", user.Id);
            foreach (var previous in await store.UnusedUserTokensAsync(user.Id, UserTokenPurpose.LoginOtp, cancellationToken))
            {
                previous.Use(now);
            }

            var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            var expiresAt = now + TimeSpan.FromMinutes(await settings.GetAsync(IdentitySettings.OtpCodeMinutes, cancellationToken));
            store.Add(new UserToken(Guid.CreateVersion7(), user.Id, UserTokenPurpose.LoginOtp, SessionManager.LoginOtpHash(user.Id, code), now, expiresAt));
            await store.SaveChangesAsync(cancellationToken);

            var tenant = tenantContext.Tenant;
            _ = await messages.QueueAsync(
                new OutboundMessageRequest(
                    MessageChannel.Email, MessagePurpose.Transactional, user.Email, MessageTemplates.LoginOtp, user.LanguageCode,
                    new Dictionary<string, object?>
                    {
                        ["name"] = user.UserName,
                        ["appName"] = tenant.Slug,
                        ["code"] = code,
                        ["expiresAt"] = expiresAt.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture),
                    },
                    nameof(User),
                    user.Id),
                cancellationToken);

            // A delivery problem is logged by the dispatcher; the caller sees success either way (no enumeration).
            Log.Security.LoginOtpSent(logger, user.Id);
            return Result.Success();
        }, cancellationToken);

    public Task<Result<OperatorPasswordReset>> ResetPasswordByOperatorAsync(string userNameOrEmail, bool sendLink, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Identity.ResetPasswordByOperator, new { SendLink = sendLink }, async scope =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            var key = userNameOrEmail?.Trim() ?? string.Empty;
            var user = key.Length == 0 ? null : await store.FindUserByUserNameAsync(key, cancellationToken);
            if (user is null && key.Contains('@', StringComparison.Ordinal))
            {
                var matches = await store.FindUsersByEmailAsync(key, cancellationToken);
                if (matches.Count > 1)
                {
                    return Errors.Identity.UserAmbiguous();
                }

                user = matches.SingleOrDefault();
            }

            if (user is null)
            {
                return Errors.Identity.UserNotFound();
            }

            scope.SetEntity("User", user.Id);
            if (sendLink)
            {
                if (user.Email is null)
                {
                    return Errors.Identity.UserEmailMissing();
                }

                var minutes = await settings.GetAsync(IdentitySettings.PasswordResetLinkMinutes, cancellationToken);
                var sent = await SendLinkAsync(store, user, UserTokenPurpose.PasswordReset, TimeSpan.FromMinutes(minutes), "reset-password", MessageTemplates.PasswordReset, cancellationToken);
                if (sent.IsFailure)
                {
                    return Result.Failure<OperatorPasswordReset>(sent.Error!);
                }

                Log.Security.PasswordResetByOperator(logger, user.Id, "link");
                return new OperatorPasswordReset(user.Id, user.UserName, null);
            }

            var password = TemporaryPassword.Generate(await passwordPolicy.GetAsync(cancellationToken));
            var valid = await passwordPolicy.ValidateAsync(user, password, cancellationToken);
            if (valid.IsFailure)
            {
                return Result.Failure<OperatorPasswordReset>(valid.Error!);
            }

            user.SetTemporaryPassword(hasher.Hash(password), PasswordFormat.Identity, timeProvider.GetUtcNow());
            await store.SaveChangesAsync(cancellationToken);
            foreach (var session in await store.OpenSessionsOfUserAsync(user.Id, cancellationToken))
            {
                var ended = await sessions.EndSessionAsync(session.Id, SessionEndReason.SecurityStampChanged, cancellationToken);
                if (ended.IsFailure)
                {
                    return Result.Failure<OperatorPasswordReset>(ended.Error!);
                }
            }

            Log.Security.PasswordResetByOperator(logger, user.Id, "temporary password");
            return new OperatorPasswordReset(user.Id, user.UserName, password);
        }, cancellationToken);

    private async Task<Result> SendLinkAsync(
        ISessionData store, User user, UserTokenPurpose purpose, TimeSpan lifetime, string page, string template, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var previous in await store.UnusedUserTokensAsync(user.Id, purpose, cancellationToken))
        {
            previous.Use(now);
        }

        var token = SecureTokens.New();
        var expiresAt = now + lifetime;
        store.Add(new UserToken(Guid.CreateVersion7(), user.Id, purpose, SecureTokens.Hash(token), now, expiresAt));
        await store.SaveChangesAsync(cancellationToken);

        var baseUrl = await settings.GetAsync(IdentitySettings.AppBaseUrl, cancellationToken);
        var tenant = tenantContext.Tenant;
        var link = $"{baseUrl}/{tenant.Slug}/{page}?token={Uri.EscapeDataString(token)}";
        var queued = await messages.QueueAsync(
            new OutboundMessageRequest(
                MessageChannel.Email, MessagePurpose.Transactional, user.Email!, template, user.LanguageCode,
                new Dictionary<string, object?>
                {
                    ["name"] = user.UserName,
                    ["appName"] = tenant.Slug,
                    ["link"] = link,
                    ["expiresAt"] = expiresAt.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture),
                },
                nameof(User),
                user.Id),
            cancellationToken);

        return queued.IsSuccess ? Result.Success() : Result.Failure(queued.Error!);
    }

    /// <returns>The user whose password was set.</returns>
    private async Task<Result<Guid>> UseTokenAsync(string token, UserTokenPurpose purpose, string password, IOperationScope scope, CancellationToken cancellationToken)
    {
        await using var store = await data.OpenAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var stored = string.IsNullOrWhiteSpace(token) ? null : await store.FindUserTokenAsync(SecureTokens.Hash(token), purpose, cancellationToken);
        var user = stored is null ? null : await store.FindUserAsync(stored.UserId, cancellationToken);
        if (stored is null || user is null || !stored.IsUsableAt(now))
        {
            return Errors.Identity.UserTokenInvalid();
        }

        scope.SetEntity("User", user.Id);
        var valid = await passwordPolicy.ValidateAsync(user, password, cancellationToken);
        if (valid.IsFailure)
        {
            return Result.Failure<Guid>(valid.Error!);
        }

        user.SetPassword(hasher.Hash(password), PasswordFormat.Identity, now);
        stored.Use(now);
        await store.SaveChangesAsync(cancellationToken);
        return user.Id;
    }
}
