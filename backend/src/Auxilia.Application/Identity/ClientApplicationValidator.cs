using Auxilia.Application.Abstractions.Identity;
using Auxilia.Diagnostics;
using Auxilia.Domain.Platform;

using Microsoft.Extensions.Logging;

namespace Auxilia.Application.Identity;

/// <summary>
/// Validates the calling client application (D-03, <c>X-Client-Id</c>): it must exist and be enabled; confidential
/// clients (web BFF, platform console) must also present their secret (verified against its hash).
/// </summary>
internal sealed class ClientApplicationValidator
{
    private readonly IClientApplicationStore clients;
    private readonly IPasswordHasher hasher;
    private readonly ILogger<ClientApplicationValidator> logger;

    public ClientApplicationValidator(IClientApplicationStore clients, IPasswordHasher hasher, ILogger<ClientApplicationValidator> logger)
    {
        this.clients = clients;
        this.hasher = hasher;
        this.logger = logger;
    }

    /// <param name="platform">
    /// True for the console sign-in: only <see cref="ClientApplicationType.PlatformConsole"/> clients; tenant sign-ins
    /// never accept that type (the two areas have separate sessions, N02).
    /// </param>
    public async Task<bool> ValidateAsync(ClientCredentials credentials, CancellationToken cancellationToken, bool platform = false)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        if (string.IsNullOrWhiteSpace(credentials.ClientId) || await clients.FindAsync(credentials.ClientId, cancellationToken) is not { } client)
        {
            return Reject(credentials.ClientId, "unknown");
        }

        if (!client.IsEnabled)
        {
            return Reject(client.ClientId, "disabled");
        }

        if ((client.Type == ClientApplicationType.PlatformConsole) != platform)
        {
            return Reject(client.ClientId, "audience");
        }

        if (!client.IsConfidential)
        {
            return true;
        }

        var valid = client.SecretHash is { } hash
            && !string.IsNullOrEmpty(credentials.ClientSecret)
            && hasher.Verify(hash, Domain.Identity.PasswordFormat.Identity, credentials.ClientSecret) != PasswordVerification.Failed;
        return valid || Reject(client.ClientId, "secret");
    }

    private bool Reject(string? clientId, string reason)
    {
        Log.Security.ClientRejected(logger, string.IsNullOrWhiteSpace(clientId) ? "-" : clientId, reason);
        return false;
    }
}
