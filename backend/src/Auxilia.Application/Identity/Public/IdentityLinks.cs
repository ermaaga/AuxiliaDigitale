using Auxilia.Application.Abstractions.Settings;

namespace Auxilia.Application.Identity.Public;

/// <summary>What other modules need to build links to the web app in e-mails (notifications, F16).</summary>
public static class IdentityLinks
{
    /// <summary>The web app base URL (<c>auth.appBaseUrl</c>): links are <c>{base}/{tenant}/…</c>.</summary>
    public static SettingDefinition<string> AppBaseUrl => IdentitySettings.AppBaseUrl;
}
