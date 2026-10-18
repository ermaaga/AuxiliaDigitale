using System.Net;

using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Application.Abstractions.Operations;
using Auxilia.Diagnostics;
using Auxilia.Domain.Mailbox;
using Auxilia.SharedKernel.Results;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Mailbox;

/// <summary>
/// The access to a provider for one piece of work (a sync part, a message read live): a fresh OAuth access token (a
/// rotated refresh token is saved before it is relied on) or the IMAP sign-in on an address resolved again and checked
/// against SSRF (the DNS answer may have changed since the account was connected).
/// </summary>
/// <remarks>
/// Failures: <c>AUX-30020</c> when the provider refuses the stored grant, <c>AUX-30002</c> when the provider is not
/// available, <c>AUX-30012</c> when the IMAP server now resolves to a refused address. Transient failures throw
/// (<see cref="HttpRequestException"/>, <see cref="MailboxProviderException"/>, socket errors).
/// </remarks>
internal sealed class MailboxAccessResolver(
    IOperationRunner operations,
    IMailboxSyncDataFactory data,
    IEnumerable<IOAuthProvider> providers,
    IConnectedAccountSecretProtector secrets,
    IHostAddressResolver resolver,
    TimeProvider clock,
    ILogger<MailboxAccessResolver> logger)
{
    public async Task<Result<MailboxAccess>> ResolveAsync(ConnectedAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.Provider == ConnectedAccountProvider.Imap)
        {
            var login = await ImapLoginAsync(account, cancellationToken);
            return login.IsFailure ? Result.Failure<MailboxAccess>(login.Error!) : MailboxAccess.Password(login.Value);
        }

        if (providers.FirstOrDefault(provider => provider.Provider == account.Provider && provider.IsConfigured) is not { } oauth)
        {
            return Errors.Mailbox.ProviderNotAvailable(ProviderNames.Of(account.Provider));
        }

        var refreshed = await oauth.RefreshAsync(secrets.Unprotect(account.ProtectedSecret), cancellationToken);
        if (refreshed.IsFailure)
        {
            return Errors.Mailbox.SyncCredentialsRefused();
        }

        if (refreshed.Value.RefreshToken is { Length: > 0 } rotated)
        {
            await SaveRotatedSecretAsync(account.Id, secrets.Protect(rotated), cancellationToken);
        }

        return MailboxAccess.OAuth(refreshed.Value.AccessToken);
    }

    private async Task<Result<ImapLogin>> ImapLoginAsync(ConnectedAccount account, CancellationToken cancellationToken)
    {
        if (account is not { ImapHost: { Length: > 0 } host, ImapPort: { } port, ImapSecurity: { } security, ImapUserName: { Length: > 0 } userName })
        {
            return Errors.Mailbox.ProviderNotAvailable(ProviderNames.Of(account.Provider));
        }

        IReadOnlyList<IPAddress> addresses = await resolver.ResolveAsync(host, cancellationToken);
        if (addresses.Count == 0)
        {
            throw new MailboxProviderException(ProviderFailure.Transient, "HostNotResolved");
        }

        if (addresses.Any(address => !OutboundAddressPolicy.IsAllowed(address)))
        {
            Log.Security.OutboundHostRefused(logger, account.UserId, "PrivateAddress");
            return Errors.Mailbox.ImapHostRefused();
        }

        return new ImapLogin(new ImapConnectionTarget(host, addresses[0], port, security), userName, secrets.Unprotect(account.ProtectedSecret));
    }

    private async Task SaveRotatedSecretAsync(Guid accountId, string protectedSecret, CancellationToken cancellationToken)
    {
        var saved = await operations.RunAsync(Operations.Mailbox.UpdateSyncRun, new { ConnectedAccountId = accountId }, async _ =>
        {
            await using var store = await data.OpenAsync(cancellationToken);
            (await store.FindAccountAsync(accountId, cancellationToken))?.RotateSecret(protectedSecret, clock.GetUtcNow());
            await store.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, cancellationToken);

        if (saved.IsFailure)
        {
            throw new InvalidOperationException(saved.Error!.Description);
        }
    }
}
