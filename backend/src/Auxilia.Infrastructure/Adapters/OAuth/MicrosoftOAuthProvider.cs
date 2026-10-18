using Auxilia.Domain.Mailbox;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auxilia.Infrastructure.Adapters.OAuth;

/// <summary>
/// "Sign in with Microsoft" (Entra, authority <c>common</c>: work, school and personal accounts) for mail and calendar
/// through Microsoft Graph (N05): delegated read-only scopes and <c>offline_access</c>. Microsoft rotates refresh tokens
/// and has no per-grant revocation endpoint for delegated tokens: disconnecting deletes the stored token (ADR 0021).
/// </summary>
internal sealed class MicrosoftOAuthProvider(
    IHttpClientFactory httpClients, IOptionsMonitor<ConnectedAccountsOptions> options, TimeProvider clock, ILogger<MicrosoftOAuthProvider> logger)
    : OAuthProviderBase(httpClients, options, clock, logger)
{
    public const string MailScope = "Mail.Read";
    public const string CalendarScope = "Calendars.Read";

    private readonly IOptionsMonitor<ConnectedAccountsOptions> options = options;

    public override ConnectedAccountProvider Provider => ConnectedAccountProvider.Microsoft;

    public override bool SupportsCalendar => true;

    protected override Uri AuthorizeEndpoint => new($"https://login.microsoftonline.com/{Authority}/oauth2/v2.0/authorize");

    protected override Uri TokenEndpoint => new($"https://login.microsoftonline.com/{Authority}/oauth2/v2.0/token");

    private string Authority => Uri.EscapeDataString(string.IsNullOrWhiteSpace(options.CurrentValue.Microsoft.Tenant) ? "common" : options.CurrentValue.Microsoft.Tenant);

    protected override OAuthClientOptions ClientOf(ConnectedAccountsOptions configured) => configured.Microsoft;

    protected override IEnumerable<string> Scopes(bool mail, bool calendar)
    {
        yield return "openid";
        yield return "email";
        yield return "profile";
        yield return "offline_access";
        yield return "User.Read";
        if (mail)
        {
            yield return MailScope;
        }

        if (calendar)
        {
            yield return CalendarScope;
        }
    }

    protected override IEnumerable<KeyValuePair<string, string>> ExtraAuthorizationParameters() =>
    [
        new("response_mode", "query"),
        new("prompt", "select_account"),
    ];

    protected override (bool Mail, bool Calendar) Capabilities(IReadOnlyCollection<string> scopes) =>
        (HasScope(scopes, MailScope), HasScope(scopes, CalendarScope));
}
