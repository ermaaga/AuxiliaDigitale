using Auxilia.Domain.Platform;

namespace Auxilia.Application.Identity.Public;

/// <summary>
/// A client application that proved itself (enabled, secret verified for confidential clients). <c>CaptchaProvider</c>
/// is what its public calls need (a public client never goes without one, F02).
/// </summary>
public sealed record CallingClient(string ClientId, ClientApplicationType Type, bool IsConfidential, string CaptchaProvider);

/// <summary>
/// The client applications (D-03) for modules with public endpoints called by external applications (Directory:
/// registration, F02). Same rules as the sign-in: unknown, disabled, wrong secret or the platform console are refused
/// and logged (<c>AUX-29012</c>).
/// </summary>
public interface IClientApplications
{
    Task<CallingClient?> AuthenticateAsync(string? clientId, string? clientSecret, CancellationToken cancellationToken);
}

internal sealed class ClientApplications(ClientApplicationValidator validator) : IClientApplications
{
    public async Task<CallingClient?> AuthenticateAsync(string? clientId, string? clientSecret, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(clientId)
            || await validator.AuthenticateAsync(new ClientCredentials(clientId.Trim(), clientSecret), cancellationToken) is not { } client
            ? null
            : new CallingClient(client.ClientId, client.Type, client.IsConfidential, client.EffectiveCaptchaProvider);
}
