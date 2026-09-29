using Auxilia.Diagnostics;
using Auxilia.SharedKernel.Domain;
using Auxilia.SharedKernel.Results;

namespace Auxilia.Domain.Platform;

public enum ClientApplicationType
{
    /// <summary>Next.js BFF of the tenant app: confidential client.</summary>
    WebBff,

    /// <summary>Next.js BFF of the platform console: confidential client.</summary>
    PlatformConsole,

    /// <summary>Mobile app: public client (no secret), refresh rotation + device binding.</summary>
    Mobile,

    /// <summary>External integration, e.g. the future registration client (D-14).</summary>
    Integration,
}

/// <summary>
/// An application allowed to call the API (header <c>X-Client-Id</c>, decision D-03; catalog <c>client_applications</c>).
/// Confidential clients store only the secret hash.
/// </summary>
public sealed class ClientApplication : AggregateRoot<Guid>
{
    public const int ClientIdMaxLength = 100;
    public const int NameMaxLength = 200;
    public const int SecretHashMaxLength = 500;
    public const int VersionMaxLength = 50;
    public const int CaptchaProviderMaxLength = 50;

    private ClientApplication(Guid id, string clientId, string name, ClientApplicationType type)
        : base(id)
    {
        ClientId = clientId;
        Name = name;
        Type = type;
        AllowedOrigins = [];
        CaptchaProvider = "none";
        IsEnabled = true;
    }

    private ClientApplication()
    {
        ClientId = Name = CaptchaProvider = string.Empty;
        AllowedOrigins = [];
    }

    public string ClientId { get; private set; }

    public string Name { get; private set; }

    public ClientApplicationType Type { get; private set; }

    public string? SecretHash { get; private set; }

    public string[] AllowedOrigins { get; private set; }

    /// <summary>Minimum app version accepted (mobile), else <c>426</c>.</summary>
    public string? MinAppVersion { get; private set; }

    /// <summary>Captcha verifier for public endpoints: <c>none</c> (trusted confidential clients) or <c>altcha</c>.</summary>
    public string CaptchaProvider { get; private set; }

    public bool IsEnabled { get; private set; }

    public bool IsConfidential => Type is ClientApplicationType.WebBff or ClientApplicationType.PlatformConsole;

    public static Result<ClientApplication> Create(Guid id, string clientId, string name, ClientApplicationType type)
    {
        if (string.IsNullOrWhiteSpace(clientId) || clientId.Length > ClientIdMaxLength)
        {
            return Errors.Tenancy.CatalogValueInvalid("clientId", "validation.clientApplication.clientId");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > NameMaxLength)
        {
            return Errors.Tenancy.CatalogValueInvalid("name", "validation.clientApplication.name");
        }

        return new ClientApplication(id, clientId.Trim(), name.Trim(), type);
    }

    public void SetSecretHash(string secretHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretHash);
        SecretHash = secretHash;
    }

    public void SetAllowedOrigins(IEnumerable<string> origins)
    {
        ArgumentNullException.ThrowIfNull(origins);
        AllowedOrigins = origins.Select(origin => origin.Trim().TrimEnd('/')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void SetMinAppVersion(string? version) => MinAppVersion = version;

    public void SetCaptchaProvider(string provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        CaptchaProvider = provider;
    }

    public void Disable() => IsEnabled = false;

    public void Enable() => IsEnabled = true;
}
