using Auxilia.Application.Abstractions.Authorization;
using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Application.Abstractions.Settings;
using Auxilia.Contracts.Mailbox;
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Mailbox;

/// <summary>
/// The caller's own connected accounts (N05, ADR 0021): "Sign in with Google / Microsoft" through the BFF (authorization
/// code + PKCE, the API is the confidential client), PEC / IMAP with a connection test, colour, disconnect. Every
/// operation works only on the caller's accounts; another user's account is "not found".
/// </summary>
public interface IConnectedAccountManager
{
    /// <param name="provider"><c>google</c> or <c>microsoft</c>.</param>
    Task<Result<StartOAuthResponse>> StartOAuthAsync(string provider, StartOAuthRequest request, CancellationToken cancellationToken);

    /// <summary>Consumes the pending request of the state, exchanges the code and connects (or reconnects) the account.</summary>
    Task<Result<ConnectedAccountResponse>> CompleteOAuthAsync(string provider, CompleteOAuthRequest request, CancellationToken cancellationToken);

    /// <summary>Tests the connection (TLS, server address checked against SSRF) and connects the account, or replaces its password.</summary>
    Task<Result<ConnectedAccountResponse>> ConnectImapAsync(ConnectImapRequest request, CancellationToken cancellationToken);

    Task<Result<ConnectedAccountResponse>> UpdateAsync(Guid id, UpdateConnectedAccountRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes the account with its secret and copied data, then revokes the grant at the provider when it can.</summary>
    Task<Result> DisconnectAsync(Guid id, CancellationToken cancellationToken);
}

public interface IConnectedAccountQueryService
{
    Task<Result<IReadOnlyList<ConnectedAccountResponse>>> ListAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ConnectedAccountProviderResponse>>> ProvidersAsync(CancellationToken cancellationToken);

    Task<Result<ImapServerSuggestionResponse>> SuggestImapServerAsync(string? address, CancellationToken cancellationToken);
}

/// <summary>Who the connected accounts belong to: always the signed-in tenant user, never an Administrator for someone else.</summary>
internal sealed class ConnectedAccountPolicy(ICurrentUser currentUser)
{
    public Guid? Me => currentUser.ActorType == ActorType.User ? currentUser.UserId : null;
}

/// <summary>Provider names in URLs and responses.</summary>
internal static class ProviderNames
{
    public static string Of(ConnectedAccountProvider provider) => provider.ToString().ToLowerInvariant();

    public static bool TryParse(string? name, out ConnectedAccountProvider provider)
    {
        provider = default;
        return !string.IsNullOrWhiteSpace(name)
            && !name.Any(char.IsDigit)
            && Enum.TryParse(name, ignoreCase: true, out provider)
            && Enum.IsDefined(provider);
    }

    public static ConnectedAccountResponse ToResponse(ConnectedAccount account) =>
        new(
            account.Id,
            Of(account.Provider),
            account.Address,
            account.DisplayName,
            account.Color,
            account.HasMail,
            account.HasCalendar,
            account.Status.ToString(),
            account.LastErrorCode,
            account.ImapHost,
            account.ImapPort,
            account.ConnectedAt,
            account.MailSyncedAt,
            account.CalendarSyncedAt);
}

internal sealed class ConnectedAccountManager(
    IOperationRunner operations,
    IConnectedAccountDataFactory data,
    IEnumerable<IOAuthProvider> providers,
    IConnectedAccountSecretProtector secrets,
    IImapConnectionTester imap,
    IHostAddressResolver resolver,
    ISettingsProvider settings,
    IAccessGuard guard,
    ConnectedAccountPolicy policy,
    TimeProvider clock,
    ILogger<ConnectedAccountManager> logger) : IConnectedAccountManager
{
    public const int PasswordMaxLength = 1000;

    public Task<Result<StartOAuthResponse>> StartOAuthAsync(string provider, StartOAuthRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<StartOAuthResponse>(Operations.Mailbox.StartOAuth, new { Provider = provider }, async _ =>
        {
            var allowed = await guard.EnsureAsync(MailboxPermissions.ConnectAccounts, cancellationToken);
            if (allowed.IsFailure || policy.Me is not { } me)
            {
                return Result.Failure<StartOAuthResponse>(allowed.Error ?? Errors.Identity.PermissionDenied());
            }

            if (OAuthProvider(provider) is not { } adapter)
            {
                return Errors.Mailbox.ProviderNotAvailable(provider);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var (mail, calendar, loginHint) = (request.Mail, request.Calendar, (string?)null);
            if (request.AccountId is { } accountId)
            {
                if (await store.FindAsync(me, accountId, cancellationToken) is not { } account || account.Provider != adapter.Provider)
                {
                    return Errors.Mailbox.AccountNotFound();
                }

                (mail, calendar, loginHint) = (mail || account.HasMail, calendar || account.HasCalendar, account.Address);
            }

            if ((!mail && !calendar) || (calendar && !adapter.SupportsCalendar))
            {
                return Errors.Mailbox.AccountInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["capabilities"] = ["validation.mailbox.capabilities"] });
            }

            var now = clock.GetUtcNow();
            var state = OAuthSecrets.NewRandom();
            var verifier = OAuthSecrets.NewRandom();
            await store.DeleteExpiredRequestsAsync(now, cancellationToken);
            store.Add(OAuthRequest.Create(Guid.CreateVersion7(), OAuthSecrets.Hash(state), me, adapter.Provider, mail, calendar, secrets.Protect(verifier), now));
            await store.SaveChangesAsync(cancellationToken);

            return new StartOAuthResponse(adapter.AuthorizationUrl(new OAuthAuthorization(state, OAuthSecrets.Challenge(verifier), mail, calendar, loginHint)));
        }, cancellationToken);
    }

    public Task<Result<ConnectedAccountResponse>> CompleteOAuthAsync(string provider, CompleteOAuthRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<ConnectedAccountResponse>(Operations.Mailbox.CompleteOAuth, new { Provider = provider }, async scope =>
        {
            var allowed = await guard.EnsureAsync(MailboxPermissions.ConnectAccounts, cancellationToken);
            if (allowed.IsFailure || policy.Me is not { } me)
            {
                return Result.Failure<ConnectedAccountResponse>(allowed.Error ?? Errors.Identity.PermissionDenied());
            }

            if (OAuthProvider(provider) is not { } adapter)
            {
                return Errors.Mailbox.ProviderNotAvailable(provider);
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var pending = string.IsNullOrWhiteSpace(request.State) || request.State.Length > 200
                ? null
                : await store.FindRequestAsync(OAuthSecrets.Hash(request.State), cancellationToken);
            if (pending is null)
            {
                Log.Security.OAuthStateRejected(logger, ProviderNames.Of(adapter.Provider), me, "Unknown");
                return Errors.Mailbox.OAuthStateInvalid();
            }

            // Consumed and saved before anything else: the same answer is never accepted twice.
            var now = clock.GetUtcNow();
            var rejection = pending.Consume(me, adapter.Provider, now);
            await store.SaveChangesAsync(cancellationToken);
            if (rejection != OAuthRequestRejection.None)
            {
                Log.Security.OAuthStateRejected(logger, ProviderNames.Of(adapter.Provider), me, rejection.ToString());
                return Errors.Mailbox.OAuthStateInvalid();
            }

            if (!string.IsNullOrEmpty(request.Error))
            {
                return Errors.Mailbox.OAuthDenied();
            }

            if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 4096)
            {
                return Errors.Mailbox.OAuthExchangeFailed();
            }

            var exchanged = await adapter.ExchangeCodeAsync(request.Code, secrets.Unprotect(pending.ProtectedVerifier), cancellationToken);
            if (exchanged.IsFailure)
            {
                return Result.Failure<ConnectedAccountResponse>(exchanged.Error!);
            }

            var tokens = exchanged.Value;
            if (string.IsNullOrWhiteSpace(tokens.Address))
            {
                return Errors.Mailbox.OAuthExchangeFailed();
            }

            // What the user actually granted (consent screens let them untick a capability), not what was asked.
            if (!tokens.HasMail && !tokens.HasCalendar)
            {
                return Errors.Mailbox.OAuthDenied();
            }

            var grant = new OAuthGrantDetails(
                tokens.Address, tokens.DisplayName, tokens.HasMail, tokens.HasCalendar, tokens.Scopes,
                string.IsNullOrEmpty(tokens.RefreshToken) ? null : secrets.Protect(tokens.RefreshToken));
            now = clock.GetUtcNow();
            var account = await store.FindByAddressAsync(me, adapter.Provider, ConnectedAccount.NormalizeAddress(tokens.Address), cancellationToken);
            if (account is not null)
            {
                account.Reconnect(grant, now);
            }
            else
            {
                if (grant.ProtectedRefreshToken is null)
                {
                    // Without a refresh token Auxilia could read the account only for an hour.
                    return Errors.Mailbox.OAuthExchangeFailed();
                }

                var created = ConnectedAccount.ConnectOAuth(
                    Guid.CreateVersion7(), me, adapter.Provider, grant, await NextColorAsync(store, me, cancellationToken), now);
                if (created.IsFailure)
                {
                    return Result.Failure<ConnectedAccountResponse>(created.Error!);
                }

                account = created.Value;
                store.Add(account);
            }

            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("ConnectedAccount", account.Id);
            var linked = ProviderNames.Of(account.Provider);
            Log.Security.ConnectedAccountLinked(logger, me, linked, account.Id);
            return ProviderNames.ToResponse(account);
        }, cancellationToken);
    }

    public Task<Result<ConnectedAccountResponse>> ConnectImapAsync(ConnectImapRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<ConnectedAccountResponse>(Operations.Mailbox.ConnectImap, null, async scope =>
        {
            var allowed = await guard.EnsureAsync(MailboxPermissions.ConnectAccounts, cancellationToken);
            if (allowed.IsFailure || policy.Me is not { } me)
            {
                return Result.Failure<ConnectedAccountResponse>(allowed.Error ?? Errors.Identity.PermissionDenied());
            }

            var security = Enum.TryParse<ImapSecurity>(request.Security, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !request.Security.Any(char.IsDigit)
                ? parsed
                : (ImapSecurity)(-1);
            var server = new ImapServer(request.Host?.Trim() ?? string.Empty, request.Port, security);
            var valid = ConnectedAccount.ValidateImap(request.Address, server, request.UserName);
            var errors = valid.IsFailure ? new Dictionary<string, string[]>(valid.Error!.ValidationErrors, StringComparer.Ordinal) : [];
            if (string.IsNullOrEmpty(request.Password) || request.Password.Length > PasswordMaxLength)
            {
                errors["password"] = ["validation.mailbox.password"];
            }

            if (!errors.ContainsKey("port") && MailboxSettings.ParsePorts(await settings.GetAsync(MailboxSettings.ImapAllowedPorts, cancellationToken)) is { } ports
                && !ports.Contains(request.Port))
            {
                errors["port"] = ["validation.mailbox.portNotAllowed"];
            }

            if (errors.Count > 0)
            {
                return Errors.Mailbox.AccountInvalid(errors);
            }

            var addresses = await resolver.ResolveAsync(server.Host, cancellationToken);
            if (addresses.Count == 0)
            {
                return Errors.Mailbox.AccountInvalid(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["host"] = ["validation.mailbox.hostUnknown"] });
            }

            // Every address is checked and the connection uses one of them: no second resolution (DNS rebinding).
            if (addresses.Any(address => !OutboundAddressPolicy.IsAllowed(address)))
            {
                Log.Security.OutboundHostRefused(logger, me, "PrivateAddress");
                return Errors.Mailbox.ImapHostRefused();
            }

            var target = new ImapConnectionTarget(server.Host, addresses[0], server.Port, server.Security);
            switch (await imap.TestAsync(target, request.UserName.Trim(), request.Password, cancellationToken))
            {
                case ImapTestOutcome.AuthenticationFailed:
                    return Errors.Mailbox.ImapAuthenticationFailed();
                case ImapTestOutcome.ConnectionFailed:
                    return Errors.Mailbox.ImapConnectionFailed();
            }

            await using var store = await data.OpenAsync(cancellationToken);
            var now = clock.GetUtcNow();
            var protectedPassword = secrets.Protect(request.Password);
            var account = await store.FindByAddressAsync(me, ConnectedAccountProvider.Imap, ConnectedAccount.NormalizeAddress(request.Address), cancellationToken);
            if (account is not null)
            {
                var reconnected = account.ReconnectImap(server, request.UserName, protectedPassword, now);
                if (reconnected.IsFailure)
                {
                    return Result.Failure<ConnectedAccountResponse>(reconnected.Error!);
                }
            }
            else
            {
                var created = ConnectedAccount.ConnectImap(
                    Guid.CreateVersion7(), me, request.Address, server, request.UserName, protectedPassword, await NextColorAsync(store, me, cancellationToken), now);
                if (created.IsFailure)
                {
                    return Result.Failure<ConnectedAccountResponse>(created.Error!);
                }

                account = created.Value;
                store.Add(account);
            }

            await store.SaveChangesAsync(cancellationToken);
            scope.SetEntity("ConnectedAccount", account.Id);
            var linked = ProviderNames.Of(account.Provider);
            Log.Security.ConnectedAccountLinked(logger, me, linked, account.Id);
            return ProviderNames.ToResponse(account);
        }, cancellationToken);
    }

    public Task<Result<ConnectedAccountResponse>> UpdateAsync(Guid id, UpdateConnectedAccountRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return operations.RunAsync<ConnectedAccountResponse>(Operations.Mailbox.UpdateAccount, new { ConnectedAccountId = id }, async _ =>
        {
            var allowed = await guard.EnsureAsync(MailboxPermissions.ConnectAccounts, cancellationToken);
            if (allowed.IsFailure || policy.Me is not { } me)
            {
                return Result.Failure<ConnectedAccountResponse>(allowed.Error ?? Errors.Identity.PermissionDenied());
            }

            await using var store = await data.OpenAsync(cancellationToken);
            if (await store.FindAsync(me, id, cancellationToken) is not { } account)
            {
                return Errors.Mailbox.AccountNotFound();
            }

            var changed = account.ChangeColor(request.Color, clock.GetUtcNow());
            if (changed.IsFailure)
            {
                return Result.Failure<ConnectedAccountResponse>(changed.Error!);
            }

            await store.SaveChangesAsync(cancellationToken);
            return ProviderNames.ToResponse(account);
        }, cancellationToken);
    }

    public Task<Result> DisconnectAsync(Guid id, CancellationToken cancellationToken) =>
        operations.RunAsync(Operations.Mailbox.DisconnectAccount, new { ConnectedAccountId = id }, async _ =>
        {
            var allowed = await guard.EnsureAsync(MailboxPermissions.ConnectAccounts, cancellationToken);
            if (allowed.IsFailure || policy.Me is not { } me)
            {
                return allowed.IsFailure ? allowed : Errors.Identity.PermissionDenied();
            }

            ConnectedAccountProvider provider;
            string protectedSecret;
            await using (var store = await data.OpenAsync(cancellationToken))
            {
                if (await store.FindAsync(me, id, cancellationToken) is not { } account)
                {
                    return Errors.Mailbox.AccountNotFound();
                }

                (provider, protectedSecret) = (account.Provider, account.ProtectedSecret);
                store.Remove(account);
                await store.SaveChangesAsync(cancellationToken);
            }

            var unlinked = ProviderNames.Of(provider);
            Log.Security.ConnectedAccountUnlinked(logger, me, unlinked, id);
            await RevokeAsync(provider, protectedSecret, id, cancellationToken);
            return Result.Success();
        }, cancellationToken);

    /// <summary>Best effort: the account is already gone; a provider that cannot be reached only leaves a warning.</summary>
    private async Task RevokeAsync(ConnectedAccountProvider provider, string protectedSecret, Guid accountId, CancellationToken cancellationToken)
    {
        if (provider == ConnectedAccountProvider.Imap || providers.FirstOrDefault(item => item.Provider == provider && item.IsConfigured) is not { } adapter)
        {
            return;
        }

        try
        {
            if (await adapter.RevokeAsync(secrets.Unprotect(protectedSecret), cancellationToken) == OAuthRevocation.Failed)
            {
                Log.Mailbox.GrantRevokeFailed(logger, null, ProviderNames.Of(provider), accountId);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Mailbox.GrantRevokeFailed(logger, exception, ProviderNames.Of(provider), accountId);
        }
    }

    private IOAuthProvider? OAuthProvider(string name) =>
        ProviderNames.TryParse(name, out var provider) && provider != ConnectedAccountProvider.Imap
            ? providers.FirstOrDefault(item => item.Provider == provider && item.IsConfigured)
            : null;

    private static async Task<string> NextColorAsync(IConnectedAccountData store, Guid userId, CancellationToken cancellationToken) =>
        ConnectedAccount.NextColor((await store.AccountsOfAsync(userId, cancellationToken)).Select(account => account.Color));
}

internal sealed class ConnectedAccountQueryService(
    IConnectedAccountDataFactory data, IEnumerable<IOAuthProvider> providers, IPermissionAccess permissions, ConnectedAccountPolicy policy)
    : IConnectedAccountQueryService
{
    public async Task<Result<IReadOnlyList<ConnectedAccountResponse>>> ListAsync(CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(MailboxPermissions.ConnectAccounts, cancellationToken) || policy.Me is not { } me)
        {
            return Errors.Identity.PermissionDenied();
        }

        await using var store = await data.OpenAsync(cancellationToken);
        return (await store.AccountsOfAsync(me, cancellationToken)).Select(ProviderNames.ToResponse).ToArray();
    }

    public async Task<Result<IReadOnlyList<ConnectedAccountProviderResponse>>> ProvidersAsync(CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(MailboxPermissions.ConnectAccounts, cancellationToken))
        {
            return Errors.Identity.PermissionDenied();
        }

        var offered = providers
            .Where(provider => provider.IsConfigured && provider.Provider != ConnectedAccountProvider.Imap)
            .OrderBy(provider => provider.Provider)
            .Select(provider => new ConnectedAccountProviderResponse(ProviderNames.Of(provider.Provider), true, provider.SupportsCalendar))
            .Append(new ConnectedAccountProviderResponse(ProviderNames.Of(ConnectedAccountProvider.Imap), true, false))
            .ToArray();
        return offered;
    }

    public async Task<Result<ImapServerSuggestionResponse>> SuggestImapServerAsync(string? address, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(MailboxPermissions.ConnectAccounts, cancellationToken))
        {
            return Errors.Identity.PermissionDenied();
        }

        return ImapServers.Suggest(address) is { } server
            ? new ImapServerSuggestionResponse(server.Host, server.Port, server.Security.ToString())
            : new ImapServerSuggestionResponse(null, null, null);
    }
}
