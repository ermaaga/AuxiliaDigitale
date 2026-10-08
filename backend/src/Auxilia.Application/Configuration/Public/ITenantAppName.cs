using Auxilia.Application.Abstractions.Settings;

namespace Auxilia.Application.Configuration.Public;

/// <summary>The tenant's application name (branding, F23), e.g. the issuer shown by authenticator apps (N04).</summary>
public interface ITenantAppName
{
    Task<string> GetAsync(CancellationToken cancellationToken);
}

internal sealed class TenantAppName(ISettingsProvider settings) : ITenantAppName
{
    public Task<string> GetAsync(CancellationToken cancellationToken) => settings.GetAsync(BrandingSettings.AppName, cancellationToken);
}
