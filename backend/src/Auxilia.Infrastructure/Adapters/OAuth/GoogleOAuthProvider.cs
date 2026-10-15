using System.Net;
using System.Net.Http;

using Auxilia.Application.Abstractions.Mailbox;
using Auxilia.Domain.Mailbox;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Auxilia.Infrastructure.Adapters.OAuth;

/// <summary>
/// "Sign in with Google" for Gmail and Google Calendar (N05): read-only scopes asked incrementally
/// (<c>include_granted_scopes</c>), offline access with <c>prompt=consent</c> so a refresh token always comes back.
/// <c>gmail.readonly</c> is a restricted scope (Google verification + yearly security assessment, ADR 0021).
/// </summary>
internal sealed class GoogleOAuthProvider(
    IHttpClientFactory httpClients, IOptionsMonitor<ConnectedAccountsOptions> options, TimeProvider clock, ILogger<GoogleOAuthProvider> logger)
    : OAuthProviderBase(httpClients, options, clock, logger)
{
    public const string MailScope = "https://www.googleapis.com/auth/gmail.readonly";
    public const string CalendarScope = "https://www.googleapis.com/auth/calendar.events.readonly";

    private static readonly Uri RevokeEndpoint = new("https://oauth2.googleapis.com/revoke");

    public override ConnectedAccountProvider Provider => ConnectedAccountProvider.Google;

    public override bool SupportsCalendar => true;

    protected override Uri AuthorizeEndpoint { get; } = new("https://accounts.google.com/o/oauth2/v2/auth");

    protected override Uri TokenEndpoint { get; } = new("https://oauth2.googleapis.com/token");

    public override async Task<OAuthRevocation> RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);

        try
        {
            var (status, json) = await PostAsync(RevokeEndpoint, [new("token", refreshToken)], cancellationToken);
            json?.Dispose();

            // 400 invalid_token: already revoked by the user, nothing left to do.
            return status is HttpStatusCode.OK or HttpStatusCode.BadRequest ? OAuthRevocation.Revoked : OAuthRevocation.Failed;
        }
        catch (Exception exception) when (exception is HttpRequestException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return OAuthRevocation.Failed;
        }
    }

    protected override OAuthClientOptions ClientOf(ConnectedAccountsOptions configured) => configured.Google;

    protected override IEnumerable<string> Scopes(bool mail, bool calendar)
    {
        yield return "openid";
        yield return "email";
        yield return "profile";
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
        new("access_type", "offline"),
        new("include_granted_scopes", "true"),
        new("prompt", "consent"),
    ];

    protected override (bool Mail, bool Calendar) Capabilities(IReadOnlyCollection<string> scopes) =>
        (HasScope(scopes, MailScope), HasScope(scopes, CalendarScope) || HasScope(scopes, "https://www.googleapis.com/auth/calendar.readonly"));
}
