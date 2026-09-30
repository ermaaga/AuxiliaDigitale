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
}

internal sealed class AccountLinkManager : IAccountLinkManager
{
    private readonly IOperationRunner operations;
    private readonly ISessionDataFactory data;
    private readonly IPasswordHasher hasher;
    private readonly IMessageDispatcher messages;
    private readonly ISessionManager sessions;
    private readonly ISettingsProvider settings;
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
        var minimumLength = await settings.GetAsync(IdentitySettings.PasswordMinLength, cancellationToken);
        if (string.IsNullOrEmpty(password) || password.Length < minimumLength)
        {
            return Errors.Identity.PasswordTooWeak(minimumLength);
        }

        user.SetPassword(hasher.Hash(password), PasswordFormat.Identity, now);
        stored.Use(now);
        await store.SaveChangesAsync(cancellationToken);
        return user.Id;
    }
}
